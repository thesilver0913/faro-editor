using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>
/// Problems (spec §11): every broken binding in the project, grouped by screen / component, next to the
/// per-node red badges. Clicking one shows that graph on the canvas and selects the node.
/// </summary>
public sealed class ProblemsView : UserControl
{
    readonly StackPanel list = new() { Spacing = 2, Margin = new(8) };

    public ProblemsView() => Content = new ScrollViewer { Content = list };

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Render;
        Workspace.RunChanged += Render;
        Render();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Render;
        Workspace.RunChanged -= Render;
        base.OnDetachedFromVisualTree(e);
    }

    void Render()
    {
        list.Children.Clear();
        if (Workspace.Issues.Count == 0 && Workspace.BuildErrors.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = L.T("No problems."), Opacity = 0.6 });
            return;
        }
        // Compiler errors from the last Run build (spec §11 lists bindings; the build is what breaks the run).
        if (Workspace.BuildErrors.Count > 0) Header($"{L.T("Build")} ({Workspace.BuildErrors.Count})");
        foreach (var error in Workspace.BuildErrors)
            Item($"{Path.GetFileName(error.File)}:{error.Line}:{error.Column} {error.Code} {error.Message}", () => CodeView.Open(error.File, error.Line, error.Column));
        foreach (var group in Workspace.Issues.GroupBy(i => i.Screen).OrderBy(g => g.Key))
        {
            Header($"{(group.Key.Length > 0 ? group.Key : L.T("Project"))} ({group.Count()})");
            foreach (var issue in group)
                Item((issue.NodeId.Length > 0 ? issue.NodeId + ": " : "") + issue.Message
                    + (issue.Suggestions.Count > 0 ? " " + L.F("Did you mean {0}?", issue.Suggestions[0]) : ""), () => Reveal(issue));
        }
    }

    void Header(string text) => list.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 2) });

    void Item(string text, Action open)
    {
        var item = new Button
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Padding = new(6, 3),
        };
        item.Click += (_, _) => open();
        list.Children.Add(item);
    }

    static void Reveal(BindingIssue issue)
    {
        if (Workspace.Project?.Graph(issue.Screen) is null) return; // e.g. a bindings file with no screen
        CanvasView.ShowScreen(issue.Screen);
        var nodeId = issue.NodeId.Split('/')[0]; // inside an instance: select the instance
        CanvasView.Select(nodeId.Length > 0 && CanvasView.ScreenNodeIds.Contains(nodeId) ? [nodeId] : []);
    }
}
