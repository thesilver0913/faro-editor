using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// Vibe coding pane (spec §8): chat with a swappable LLM, generated files shown as diffs, applied only
/// after approval. Approval is blocked on Roslyn syntax errors (§11.5) and while the target class has
/// unsaved manual edits (§8.5). The transcript is static so it survives re-docking.
/// </summary>
public sealed class ChatView : UserControl
{
    sealed record Proposal(string Path, string Relative, string Code);

    static FaroSettings settings => FaroSettings.Current;
    static readonly List<ChatTurn> history = [];
    static readonly List<Proposal> pending = [];
    static string? error;
    static CancellationTokenSource? running;
    static string draft = "";
    static event Action? Changed;

    readonly StackPanel transcript = new() { Spacing = 10, Margin = new(12) };
    readonly ScrollViewer scroller;
    readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 160, PlaceholderText = L.T("Describe the class or method you need… (Ctrl+Enter to send)") };
    readonly Button send = new() { Content = L.T("Send"), VerticalAlignment = VerticalAlignment.Stretch };
    readonly ComboBox lifetime = new() { ItemsSource = Enum.GetNames<Lifetime>(), SelectedItem = nameof(Lifetime.ScreenScoped) };
    readonly CheckBox persistent = new() { Content = L.T("Persistent") };

    public ChatView()
    {
        var provider = new ComboBox { ItemsSource = new[] { "Claude", "OpenAI-compatible" }, SelectedItem = settings.Provider };
        var model = new TextBox { MinWidth = 160 };
        var baseUrl = new TextBox { MinWidth = 220, PlaceholderText = L.T("Base URL") };
        void ShowSettings()
        {
            var claude = settings.Provider == "Claude";
            model.Text = claude ? settings.ClaudeModel : settings.OpenAiModel;
            model.PlaceholderText = L.T(claude ? "Model" : "Model name");
            baseUrl.Text = settings.OpenAiBaseUrl;
            baseUrl.IsVisible = !claude;
        }
        ShowSettings();
        provider.SelectionChanged += (_, _) => { settings.Provider = (string)provider.SelectedItem!; settings.Save(); ShowSettings(); };
        model.LostFocus += (_, _) =>
        {
            if (settings.Provider == "Claude") settings.ClaudeModel = model.Text ?? ""; else settings.OpenAiModel = model.Text ?? "";
            settings.Save();
        };
        baseUrl.LostFocus += (_, _) => { settings.OpenAiBaseUrl = baseUrl.Text ?? ""; settings.Save(); };

        send.Click += (_, _) => { if (running is null) Send(); else running.Cancel(); };
        input.Text = draft;
        input.TextChanged += (_, _) => draft = input.Text ?? "";
        // Tunnel: a multi-line TextBox consumes Enter before a bubbling handler would see it.
        input.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Send(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var bar = new WrapPanel
        {
            ItemSpacing = 8, LineSpacing = 8, Margin = new(8),
            Children = { provider, model, baseUrl, new TextBlock { Text = L.T("New classes:"), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 }, lifetime, persistent },
        };
        var bottom = new DockPanel { Margin = new(8) };
        DockPanel.SetDock(send, Avalonia.Controls.Dock.Right);
        bottom.Children.Add(send);
        bottom.Children.Add(input);
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom);
        scroller = new ScrollViewer { Content = transcript };
        Content = new DockPanel { Children = { bar, bottom, scroller } };
    }

    /// <summary>Opens the chat with a request pre-filled, e.g. from a broken binding on the canvas (spec §8 round trip).</summary>
    public static void Prefill(string text)
    {
        draft = text;
        Changed?.Invoke();
        ConsoleView.Show(ConsoleView.Tab.VibeCoding);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Changed += Render;
        Workspace.Changed += Render; // a save may lift the edit lock on a pending proposal
        Render();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Changed -= Render;
        Workspace.Changed -= Render;
        base.OnDetachedFromVisualTree(e);
    }

    async void Send()
    {
        var text = input.Text?.Trim();
        if (string.IsNullOrEmpty(text) || running is not null || Workspace.Project is not { } project) return;
        if (LockedClasses().FirstOrDefault(c => text.Contains(c)) is { } locked)
        {
            error = L.F("`{0}` is being edited in the code editor (unsaved changes). Save it first, then ask again.", locked);
            Changed?.Invoke();
            return;
        }
        history.Add(new(true, text));
        history.Add(new(false, ""));
        pending.Clear();
        error = null;
        draft = "";
        running = new();
        Changed?.Invoke();
        try
        {
            var system = VibeCoding.SystemPrompt(project, Workspace.Root, Enum.Parse<Lifetime>((string)lifetime.SelectedItem!), persistent.IsChecked == true);
            await foreach (var chunk in settings.CreateProvider().Stream(system, history[..^1], running.Token))
            {
                history[^1] = history[^1] with { Text = history[^1].Text + chunk };
                Changed?.Invoke();
            }
            foreach (var file in VibeCoding.ParseFiles(history[^1].Text))
                if (VibeCoding.ResolvePath(Workspace.Root, file.Path) is { } path) pending.Add(new(path, file.Path, file.Code));
                else error = L.F("Ignored {0}: generated files must be .cs files under Source/.", file.Path);
        }
        catch (OperationCanceledException) { error = L.T("Stopped."); }
        catch (Exception e) { error = e.Message; }
        finally
        {
            if (history[^1].Text.Length == 0) history.RemoveRange(history.Count - 2, 2); // keep the transcript valid for the next request
            running = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Classes in files with unsaved manual edits: AI changes to them are held back (spec §8.5).</summary>
    static IEnumerable<string> LockedClasses() =>
        Directory.Exists(Path.Combine(Workspace.Root, "Source"))
            ? Directory.EnumerateFiles(Path.Combine(Workspace.Root, "Source"), "*.*", SearchOption.AllDirectories)
                .Where(CodeView.IsDirty).SelectMany(f => f.EndsWith(".java") ? JavaProject.ClassNames(File.ReadAllText(f)) : VibeCoding.ClassNames(File.ReadAllText(f)))
            : [];

    void Render()
    {
        input.Text = draft;
        send.Content = L.T(running is null ? "Send" : "Stop");
        transcript.Children.Clear();
        foreach (var turn in history)
            transcript.Children.Add(new Border
            {
                Background = new SolidColorBrush(turn.IsUser ? Color.Parse("#1473E6") : Color.Parse("#333333")),
                CornerRadius = new(6),
                Padding = new(10, 6),
                HorizontalAlignment = turn.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                Child = new SelectableTextBlock { Text = turn.Text.Length > 0 ? turn.Text : "…", TextWrapping = TextWrapping.Wrap },
            });
        foreach (var p in pending) transcript.Children.Add(ProposalCard(p));
        if (error is not null) transcript.Children.Add(new SelectableTextBlock { Text = error, Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap });
        scroller.ScrollToEnd();
    }

    Control ProposalCard(Proposal p)
    {
        var before = File.Exists(p.Path) ? File.ReadAllText(p.Path) : "";
        var java = p.Path.EndsWith(".java");
        var syntax = java ? [] : VibeCoding.SyntaxErrors(p.Code); // ponytail: no Java syntax check before approval; the build reports errors
        var locked = CodeView.IsDirty(p.Path) ? (java ? JavaProject.ClassNames(before) : VibeCoding.ClassNames(before)).FirstOrDefault() ?? Path.GetFileName(p.Path) : null;

        var diff = new SelectableTextBlock { FontFamily = FontFamily.Parse("Cascadia Code,Consolas,Menlo,monospace"), FontSize = 12 };
        foreach (var (op, line) in VibeCoding.Diff(before, p.Code))
            diff.Inlines!.Add(new Run($"{op} {line}\n") { Foreground = op switch { '+' => Brushes.LightGreen, '-' => Brushes.IndianRed, _ => Brushes.Gray } });

        var approve = new Button { Content = L.T("Approve"), IsEnabled = syntax.Count == 0 && locked is null };
        var reject = new Button { Content = L.T("Reject") };
        approve.Click += (_, _) =>
        {
            CodeView.ApplyGenerated(p.Path, p.Code);
            pending.Remove(p);
            Changed?.Invoke();
        };
        reject.Click += (_, _) => { pending.Remove(p); Changed?.Invoke(); };

        var notes = syntax.Select(e => L.T("Syntax error, ") + e).ToList();
        if (locked is not null) notes.Add($"`{locked}` is being edited in the code editor (unsaved changes). Save it to approve.");
        return new Border
        {
            BorderBrush = Brushes.Gray, BorderThickness = new(1), CornerRadius = new(6), Padding = new(10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = L.T(before.Length == 0 ? "New file: " : "Change: ") + p.Relative, FontWeight = FontWeight.SemiBold },
                    new ScrollViewer { MaxHeight = 320, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = diff },
                    new TextBlock { Text = string.Join("\n", notes), Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, IsVisible = notes.Count > 0 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { approve, reject } },
                },
            },
        };
    }
}
