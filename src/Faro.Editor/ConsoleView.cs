using Avalonia.Controls;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>VS Code-style panel: Problems, Output (the running app), AI Chat and History as tabs side by side in one pane.</summary>
public sealed class ConsoleView : UserControl
{
    public enum Tab { Problems, Output, VibeCoding, History }

    static event Action<Tab>? Requested;

    /// <summary>Switches the console to a tab (e.g. AI Chat when a request is pre-filled).</summary>
    public static void Show(Tab tab) => Requested?.Invoke(tab);

    /// <summary>Run / Stop (canvas button, F5): the output shows while it runs.</summary>
    public static void RunOrStop()
    {
        if (Workspace.Running) { Workspace.Stop(); return; }
        Workspace.Run();
        Show(Tab.Output);
    }

    readonly TabControl tabs = new() { Padding = new(0) };
    readonly TabItem problems = Item(L.T("Problems"), new ProblemsView());
    readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12, BorderThickness = new(0) };

    public ConsoleView()
    {
        tabs.ItemsSource = new[] { problems, Item(L.T("Output"), output), Item(L.T("AI Chat"), new ChatView()), Item(L.T("History"), new HistoryView()) };
        tabs.SelectedIndex = (int)Tab.VibeCoding;
        Content = tabs;
    }

    static TabItem Item(string header, Control content) =>
        new() { Header = header, Content = content, FontSize = 13, MinHeight = 30, Padding = new(10, 4) };

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Requested += Select;
        Workspace.Changed += Count;
        Workspace.RunChanged += Count;
        Workspace.OutputLine += Append;
        output.Text = string.Join('\n', Workspace.Output);
        Count();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Requested -= Select;
        Workspace.Changed -= Count;
        Workspace.RunChanged -= Count;
        Workspace.OutputLine -= Append;
        base.OnDetachedFromVisualTree(e);
    }

    void Select(Tab tab) => tabs.SelectedIndex = (int)tab;

    void Count()
    {
        var count = Workspace.Issues.Count + Workspace.BuildErrors.Count;
        problems.Header = count > 0 ? $"{L.T("Problems")} ({count})" : L.T("Problems");
        if (Workspace.Running && Workspace.Output.Count == 0) output.Text = ""; // a new run
    }

    void Append(string line)
    {
        output.Text = string.IsNullOrEmpty(output.Text) ? line : output.Text + "\n" + line;
        output.CaretIndex = output.Text.Length; // keep the latest line in view
    }
}
