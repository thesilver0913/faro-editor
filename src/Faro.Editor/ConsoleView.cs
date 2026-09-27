using Avalonia.Controls;

namespace Faro.Editor;

/// <summary>VS Code-style panel: Problems and Vibe Coding as tabs side by side in one pane.</summary>
public sealed class ConsoleView : UserControl
{
    public enum Tab { Problems, VibeCoding }

    static event Action<Tab>? Requested;

    /// <summary>Switches the console to a tab (e.g. Vibe Coding when a request is pre-filled).</summary>
    public static void Show(Tab tab) => Requested?.Invoke(tab);

    readonly TabControl tabs = new() { Padding = new(0) };
    readonly TabItem problems = Item("Problems", new ProblemsView());

    public ConsoleView()
    {
        tabs.ItemsSource = new[] { problems, Item("Vibe Coding", new ChatView()) };
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
        Count();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Requested -= Select;
        Workspace.Changed -= Count;
        base.OnDetachedFromVisualTree(e);
    }

    void Select(Tab tab) => tabs.SelectedIndex = (int)tab;

    void Count() => problems.Header = Workspace.Issues.Count > 0 ? $"Problems ({Workspace.Issues.Count})" : "Problems";
}
