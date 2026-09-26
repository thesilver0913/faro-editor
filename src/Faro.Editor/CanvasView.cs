using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// Canvas pane: renders the selected UIGraph with the same builder the runtime uses and marks
/// nodes with broken bindings with a red badge (hover shows the reason and suggestions).
/// </summary>
public sealed class CanvasView : UserControl
{
    readonly ComboBox screens = new() { MinWidth = 180 };
    readonly Button sync = new();
    readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
    readonly Border artboard = new() { Width = 420, MinHeight = 720, Background = Brushes.White, [TextElement.ForegroundProperty] = Brushes.Black, Margin = new(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };

    public CanvasView()
    {
        var run = new Button { Content = "Run" };
        run.Click += (_, _) => Process.Start(new ProcessStartInfo("dotnet", ["watch", "run", "--non-interactive"]) { WorkingDirectory = Workspace.Root });
        sync.Click += (_, _) =>
        {
            if (Workspace.Project is { } p) ComponentSync.Sync(p).ForEach(FaroProject.Save);
        };
        screens.SelectionChanged += (_, _) => Render();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(8), Children = { screens, sync, run, status } };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { bar, new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = artboard } } } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Refresh;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }

    void Refresh()
    {
        var selected = screens.SelectedItem as string;
        screens.ItemsSource = Workspace.Project?.Screens.Keys.Order().ToList() ?? [];
        screens.SelectedItem = selected ?? Workspace.Project?.Screens.Keys.Order().FirstOrDefault();
        var outOfDate = Workspace.Project is { } p ? ComponentSync.OutOfDate(p).Count : 0;
        sync.Content = $"Sync components ({outOfDate})";
        sync.IsEnabled = outOfDate > 0;
        status.Text = Workspace.LoadError ?? $"{Workspace.Issues.Count} broken binding(s) · {Workspace.Registry.Count} registry members";
        Render();
    }

    void Render()
    {
        if (Workspace.Project?.Screens.GetValueOrDefault(screens.SelectedItem as string ?? "") is not { } graph)
        {
            artboard.Child = new TextBlock { Text = $"No UI/*.xml screens in {Workspace.Root}", Margin = new(16), Foreground = Brushes.Gray };
            return;
        }
        var byId = new Dictionary<string, Control>();
        artboard.Child = UiBuilder.Build(graph.Root!.Element("Node")!, byId, Workspace.Root);
        foreach (var group in Workspace.Issues.GroupBy(i => i.NodeId))
            if (byId.TryGetValue(group.Key, out var control))
            {
                var badge = Badge(group);
                if (group.Select(i => VibeRequest(i, control)).FirstOrDefault(r => r is not null) is { } request)
                {
                    badge.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                    badge.PointerPressed += (_, _) => ChatView.Prefill(request);
                    ToolTip.SetTip(badge, ToolTip.GetTip(badge) + "\n\nClick to create it with vibe coding.");
                }
                AdornerLayer.SetIsClipEnabled(badge, false);
                AdornerLayer.SetAdorner(control, badge);
            }
    }

    /// <summary>
    /// The chat request for a binding whose target member is missing, with the context the canvas knows:
    /// node, event or bound property and its type (spec §8 round trip). Null for other kinds of issue.
    /// </summary>
    static string? VibeRequest(BindingIssue issue, Control control)
    {
        var bind = Workspace.Project?.Binds.FirstOrDefault(b => (string?)b.Attribute("nodeId") == issue.NodeId && (string?)b.Attribute("target") == issue.Target);
        if (bind is null || issue.Target.StartsWith("Navigate:")) return null;
        var type = Workspace.Project!.Screens.Values.SelectMany(d => d.Descendants("Node")).FirstOrDefault(n => (string?)n.Attribute("id") == issue.NodeId)?.Attribute("type")?.Value;
        var node = $"node `{issue.NodeId}` ({type})";
        if ((string?)bind.Attribute("event") is { } eventName)
            return $"Create `{issue.Target}`: a public parameterless method that runs on {eventName} of {node}.";
        var prop = (string?)bind.Attribute("prop") ?? "";
        var propType = AvaloniaPropertyRegistry.Instance.FindRegistered(control, prop)?.PropertyType.Name ?? "object";
        return $"Create `{issue.Target}`: a public {propType} property, bound {(string?)bind.Attribute("mode") ?? "OneWay"} to {prop} of {node}.";
    }

    static Control Badge(IEnumerable<BindingIssue> issues) => new Border
    {
        Width = 14,
        Height = 14,
        CornerRadius = new(7),
        Background = Brushes.Red,
        BorderBrush = Brushes.White,
        BorderThickness = new(2),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new(0, -7, -7, 0),
        [ToolTip.TipProperty] = string.Join("\n\n", issues.Select(i =>
            i.Message + (i.Suggestions.Count > 0 ? "\nDid you mean: " + string.Join(", ", i.Suggestions) : ""))),
    };
}
