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
        Render();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Render;
        base.OnDetachedFromVisualTree(e);
    }

    void Render()
    {
        list.Children.Clear();
        if (Workspace.Issues.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = "No problems.", Opacity = 0.6 });
            return;
        }
        foreach (var group in Workspace.Issues.GroupBy(i => i.Screen).OrderBy(g => g.Key))
        {
            list.Children.Add(new TextBlock { Text = $"{(group.Key.Length > 0 ? group.Key : "Project")} ({group.Count()})", FontWeight = FontWeight.SemiBold, Margin = new(0, 6, 0, 2) });
            foreach (var issue in group)
            {
                var text = (issue.NodeId.Length > 0 ? issue.NodeId + ": " : "") + issue.Message
                    + (issue.Suggestions.Count > 0 ? $" Did you mean {issue.Suggestions[0]}?" : "");
                var item = new Button
                {
                    Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    Padding = new(6, 3),
                };
                item.Click += (_, _) => Reveal(issue);
                list.Children.Add(item);
            }
        }
    }

    static void Reveal(BindingIssue issue)
    {
        if (Workspace.Project?.Graph(issue.Screen) is null) return; // e.g. a bindings file with no screen
        CanvasView.ShowScreen(issue.Screen);
        CanvasView.Select(issue.NodeId.Length > 0 && CanvasView.ScreenNodeIds.Contains(issue.NodeId) ? [issue.NodeId] : []);
    }
}
