using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Faro.Editor;

/// <summary>
/// Source control (VS Code's basic Git view) through the git command line: the branch, the changed files (click: the diff),
/// commit all with a message, pull, push, and "Initialize Repository" for a folder without one. Off in Restricted Mode:
/// a repository's own settings (hooks, fsmonitor) can run commands.
/// </summary>
public sealed class GitView : UserControl
{
    static GitView() => Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0"); // no hidden password prompt: fail and say so

    readonly TextBlock branch = new() { FontWeight = FontWeight.SemiBold, Margin = new(8, 4) };
    readonly TextBox message = new() { PlaceholderText = L.T("Commit message"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 120 };
    readonly Button commit = new() { Content = L.T("Commit All"), Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ListBox changes = new();
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new(8, 4) };
    readonly Button init = new() { Content = L.T("Initialize Repository"), Margin = new(8), IsVisible = false };
    readonly Control[] repoOnly;
    List<(string Code, string Path)> files = [];

    public GitView()
    {
        commit.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(message.Text)) { status.Text = L.T("Write a commit message first."); return; }
            if (await Run("add", "-A") == 0 && await Run("commit", "-m", message.Text.Trim()) == 0) message.Text = "";
            await Refresh();
        };
        init.Click += async (_, _) => { await Run("init"); await Refresh(); };
        changes.SelectionChanged += async (_, _) =>
        {
            if (changes.SelectedIndex < 0 || changes.SelectedIndex >= files.Count) return;
            var file = files[changes.SelectedIndex];
            changes.SelectedIndex = -1;
            await ShowDiff(file.Code, file.Path);
        };
        var bar = Icons.Toolbar(new WrapPanel
        {
            ItemSpacing = 4, LineSpacing = 4, Margin = new(4),
            Children =
            {
                Icons.Button(FASymbol.Refresh, "Refresh", async () => await Refresh()),
                Icons.Button(FASymbol.Download, "Pull", async () => { await Run("pull"); await Refresh(); }),
                Icons.Button(FASymbol.Upload, "Push", async () => { await Run("push"); await Refresh(); }),
            },
        });
        var form = new StackPanel { Spacing = 6, Margin = new(8, 0), Children = { message, commit } };
        repoOnly = [bar, form, changes];
        var top = new StackPanel { Children = { bar, branch, init, form, status } };
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { top, changes } };
    }

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiHistory.Changed += Changed;
        await Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UiHistory.Changed -= Changed;
        base.OnDetachedFromVisualTree(e);
    }

    async void Changed() => await Refresh();

    async Task Refresh()
    {
        if (!Workspace.Trusted)
        {
            Show(repo: false);
            branch.Text = L.T("Git is off in Restricted Mode.");
            return;
        }
        var lines = new List<string>();
        var code = await Git(lines, "status", "--porcelain=v1", "-b", "-uall");
        var repo = code == 0;
        Show(repo);
        init.IsVisible = code > 0; // git runs, but the folder isn't a repository
        if (!repo)
        {
            branch.Text = code < 0 ? L.T("Git isn't installed (git-scm.com).") : L.T("This folder isn't a Git repository.");
            return;
        }
        (var name, files) = ParseStatus(lines);
        branch.Text = L.F("Branch: {0}", name);
        changes.ItemsSource = files.Select(f => new DockPanel
        {
            Children =
            {
                new TextBlock { Text = f.Code, Width = 28, FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace"), Foreground = f.Code.Contains('D') ? Brushes.IndianRed : f.Code is "??" or "A" ? Brushes.SeaGreen : Brushes.Goldenrod },
                new TextBlock { Text = f.Path, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        }).ToList();
        if (files.Count == 0) status.Text = L.T("No changes.");
        else if (status.Text == L.T("No changes.")) status.Text = "";
    }

    void Show(bool repo)
    {
        foreach (var control in repoOnly) control.IsVisible = repo;
        init.IsVisible = false;
    }

    /// <summary>"## main...origin/main [ahead 1]" and "XY path" lines of `git status --porcelain=v1 -b`: the branch line as shown, and each file's code and path (a rename: the new path).</summary>
    public static (string Branch, List<(string Code, string Path)> Files) ParseStatus(IEnumerable<string> lines)
    {
        var branch = "";
        var files = new List<(string, string)>();
        foreach (var line in lines)
            if (line.StartsWith("## ")) branch = line[3..].Replace("No commits yet on ", "");
            else if (line.Length > 3) files.Add((line[..2].Trim(), line[3..].Split(" -> ")[^1].Trim('"')));
        return (branch, files);
    }

    /// <summary>A git command in the project; its output goes to the status line. Returns the exit code.</summary>
    async Task<int> Run(params string[] args)
    {
        var lines = new List<string>();
        status.Text = "git " + args[0] + "…";
        var code = await Git(lines, args);
        status.Text = lines.Count > 0 ? string.Join('\n', lines.TakeLast(6)) : code == 0 ? L.T("Done.") : "";
        return code;
    }

    static Task<int> Git(List<string> lines, params string[] args) =>
        AndroidApk.Exec("git", ["-C", Workspace.Root, .. args], line => { lock (lines) lines.Add(line); }, CancellationToken.None);

    async Task ShowDiff(string code, string path)
    {
        var lines = new List<string>();
        if (code == "??") lines.AddRange(File.Exists(Path.Combine(Workspace.Root, path)) ? File.ReadLines(Path.Combine(Workspace.Root, path)).Take(2000).Select(l => "+" + l) : []);
        else if (await Git(lines, "diff", "HEAD", "--", path) != 0) { lines.Clear(); await Git(lines, "diff", "--cached", "--", path); } // no commit yet
        var text = new TextBox
        {
            Text = string.Join('\n', lines), IsReadOnly = true, AcceptsReturn = true, Width = 600, Height = 420,
            FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12,
        };
        if (TopLevel.GetTopLevel(this) is Window owner) await Dialogs.Info(owner, path, text);
    }
}
