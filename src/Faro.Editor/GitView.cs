using System.Xml.Linq;
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
        // git names files from the repository's top; the project may be a folder inside it: paths relative to the project instead.
        var top = new List<string>();
        if (await Git(top, "rev-parse", "--show-toplevel") == 0 && top.Count > 0)
            files = [.. files.Select(f => (f.Code, Path.GetRelativePath(Workspace.Root, Path.Combine(top[0], f.Path)).Replace('\\', '/')))];
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

    /// <summary>
    /// A screen, component or bindings file's change as a reviewer reads it (Figma's version history rather than XML lines):
    /// nodes added, removed, moved or reordered, and their changed attributes and texts; an instance's snapshot counts as one
    /// "synced" change. Bindings by node and event/property. Empty when either side isn't readable XML.
    /// </summary>
    public static List<string> UiChanges(string? before, string? after)
    {
        XElement? Root(string? text)
        {
            try { return text is null ? null : XDocument.Parse(text).Root; }
            catch (System.Xml.XmlException) { return null; }
        }
        var (old, now) = (Root(before), Root(after));
        if (old is null && now is null || before is not null && old is null || after is not null && now is null) return [];
        var changes = new List<string>();
        if ((old ?? now)!.Name == "Bindings")
        {
            static Dictionary<string, XElement> Binds(XElement? root) => root?.Elements("Bind")
                .GroupBy(b => $"{(string?)b.Attribute("nodeId")} · {(string?)b.Attribute("event") ?? (string?)b.Attribute("prop")}").ToDictionary(g => g.Key, g => g.First()) ?? [];
            var (was, isNow) = (Binds(old), Binds(now));
            foreach (var (key, bind) in isNow.Where(b => !was.ContainsKey(b.Key))) changes.Add("+ " + L.F("Binding added: {0} → {1}", key, (string?)bind.Attribute("target")));
            foreach (var key in was.Keys.Where(k => !isNow.ContainsKey(k))) changes.Add("− " + L.F("Binding removed: {0}", key));
            foreach (var (key, bind) in isNow.Where(b => was.ContainsKey(b.Key)))
                if (Attributes(was[key], bind) is { Count: > 0 } diff) changes.Add("~ " + key + ": " + string.Join(", ", diff));
            return changes;
        }
        // Nodes by id, not counting the copies inside an instance (its snapshot of the master).
        static Dictionary<string, XElement> Nodes(XElement? root)
        {
            var nodes = new Dictionary<string, XElement>();
            void Walk(XElement parent)
            {
                foreach (var n in parent.Elements("Node"))
                {
                    nodes.TryAdd((string?)n.Attribute("id") ?? "", n);
                    if ((string?)n.Attribute("type") != "Instance") Walk(n);
                }
            }
            if (root is not null) Walk(root);
            return nodes;
        }
        static string Name(XElement n) => $"{(string?)n.Attribute("id")} ({((string?)n.Attribute("type") == "Instance" ? (string?)n.Attribute("component") : ((string?)n.Attribute("type"))?.Split('.')[^1])})";
        static string? Parent(XElement n) => n.Parent?.Name == "Node" ? (string?)n.Parent.Attribute("id") : null;
        if (old is not null && now is not null && (string?)old.Attribute("id") != (string?)now.Attribute("id"))
            changes.Add("~ " + L.F("Renamed {0} → {1}", (string?)old.Attribute("id"), (string?)now.Attribute("id")));
        var (before2, after2) = (Nodes(old), Nodes(now));
        foreach (var (id, n) in after2.Where(n => !before2.ContainsKey(n.Key)))
            changes.Add("+ " + (Parent(n) is { } p ? L.F("Added {0} to {1}", Name(n), p) : L.F("Added {0}", Name(n))));
        foreach (var (id, n) in before2.Where(n => !after2.ContainsKey(n.Key))) changes.Add("− " + L.F("Removed {0}", Name(n)));
        foreach (var (id, n) in after2.Where(n => before2.ContainsKey(n.Key)))
        {
            var was = before2[id];
            var diff = Attributes(was, n);
            Dictionary<string, string?> Values(XElement e, string element, string key, string value) =>
                e.Elements(element).GroupBy(p => (string?)p.Attribute(key) ?? "").ToDictionary(g => g.Key, g => (string?)g.First().Attribute(value));
            foreach (var (element, key, value) in new[] { ("Prop", "name", "value"), ("Override", "prop", "value") })
            {
                var (a, b) = (Values(was, element, key, value), Values(n, element, key, value));
                foreach (var name in a.Keys.Union(b.Keys).Where(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k)))
                    diff.Add($"{name} \"{a.GetValueOrDefault(name) ?? L.T("(none)")}\" → \"{b.GetValueOrDefault(name) ?? L.T("(none)")}\"");
            }
            if ((string?)n.Attribute("type") == "Instance" && was.Element("Node")?.ToString() != n.Element("Node")?.ToString()) diff.Add(L.T("synced with its component"));
            if (Parent(was) != Parent(n)) diff.Add(L.F("moved into {0}", Parent(n) ?? "—"));
            if (diff.Count > 0) changes.Add("~ " + Name(n) + ": " + string.Join(", ", diff));
        }
        // Same children, new order (inserting or removing a sibling isn't a reorder).
        foreach (var (id, n) in after2.Where(n => before2.ContainsKey(n.Key)))
        {
            var kept = n.Elements("Node").Select(c => (string?)c.Attribute("id")).Where(c => c is not null && before2.ContainsKey(c) && Parent(before2[c]!) == id).ToList();
            var order = before2[id].Elements("Node").Select(c => (string?)c.Attribute("id")).Where(kept.Contains).ToList();
            if ((string?)n.Attribute("type") != "Instance" && !kept.SequenceEqual(order)) changes.Add("~ " + L.F("Reordered the children of {0}", Name(n)));
        }
        return changes;
    }

    /// <summary>"width 120 → 200", "background (none) → #6750A4" for the attributes that differ.</summary>
    static List<string> Attributes(XElement before, XElement after) =>
        [.. before.Attributes().Select(a => a.Name.LocalName).Union(after.Attributes().Select(a => a.Name.LocalName))
            .Where(name => (string?)before.Attribute(name) != (string?)after.Attribute(name))
            .Select(name => $"{name} {(string?)before.Attribute(name) ?? L.T("(none)")} → {(string?)after.Attribute(name) ?? L.T("(none)")}")];

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
        // Screens, components and bindings: what changed as nodes and bindings first, the XML below.
        if (path.EndsWith(".xml") && path.Split('/')[0] is "UI" or "Bindings")
        {
            var head = new List<string>();
            var before = code is "??" or "A" || await Git(head, "show", "HEAD:./" + path) != 0 ? null : string.Join('\n', head);
            var file = Path.Combine(Workspace.Root, path);
            if (UiChanges(before, File.Exists(file) ? File.ReadAllText(file) : null) is { Count: > 0 } changes)
                lines = [.. changes, "", "── XML ──", .. lines];
        }
        var text = new TextBox
        {
            Text = string.Join('\n', lines), IsReadOnly = true, AcceptsReturn = true, Width = 600, Height = 420,
            FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12,
        };
        if (TopLevel.GetTopLevel(this) is Window owner) await Dialogs.Info(owner, path, text);
    }
}
