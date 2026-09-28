using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Faro.Editor;

/// <summary>
/// The node tree of the screen on the canvas (Figma's layers / Android Studio's component tree): selection follows
/// the canvas both ways, drag a row to reorder or move it into another container, right-click for the canvas menu,
/// Delete / F2. Instances are one row (their inside belongs to the master component).
/// </summary>
public sealed class LayersView : UserControl
{
    static readonly DataFormat<string> NodeFormat = DataFormat.CreateInProcessFormat<string>("faro-node");
    static readonly HashSet<string> collapsed = [];

    readonly TreeView tree = new() { SelectionMode = SelectionMode.Multiple, [ScrollViewer.HorizontalScrollBarVisibilityProperty] = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    readonly TextBlock title = new() { Opacity = 0.7, Margin = new(8, 4), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    readonly Dictionary<string, TreeViewItem> items = [];
    readonly Dictionary<string, string> parentOf = [];
    string? shownScreen;
    bool syncing;

    public LayersView()
    {
        tree.SelectionChanged += (_, _) =>
        {
            if (!syncing) CanvasView.Select(tree.SelectedItems!.OfType<TreeViewItem>().Select(i => (string)i.Tag!));
        };
        tree.ContextRequested += (_, e) => { CanvasView.ShowMenu(tree); e.Handled = true; };
        tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) CanvasView.DeleteSelection();
            else if (e.Key == Key.F2) CanvasView.RenameSelected();
            else return;
            e.Handled = true;
        };
        DragDrop.SetAllowDrop(tree, true);
        tree.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = Target(e) is null ? DragDropEffects.None : DragDropEffects.Move);
        tree.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.DataTransfer.TryGetValue(NodeFormat) is { } id && Target(e) is { } t)
                CanvasView.Edit("Move", (_, screen) => CanvasEdit.MoveTo(screen, id, t.Parent, t.Index) ? [screen] : []);
        });
        DockPanel.SetDock(title, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { title, tree } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Build;
        CanvasView.SelectionChanged += OnSelection;
        Build();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Build;
        CanvasView.SelectionChanged -= OnSelection;
        base.OnDetachedFromVisualTree(e);
    }

    void OnSelection()
    {
        if (CanvasView.CurrentScreen != shownScreen) Build();
        else ShowSelection();
    }

    void Build()
    {
        shownScreen = CanvasView.CurrentScreen;
        title.Text = shownScreen ?? "";
        items.Clear();
        parentOf.Clear();
        tree.ItemsSource = CanvasView.CurrentGraph()?.Root?.Element("Node") is { } root ? new[] { Item(root) } : [];
        ShowSelection();
    }

    void ShowSelection()
    {
        syncing = true;
        tree.SelectedItems!.Clear();
        foreach (var id in CanvasView.Selection)
            if (items.TryGetValue(id, out var item))
            {
                for (var p = id; parentOf.TryGetValue(p, out var parent); p = parent) items[parent].IsExpanded = true;
                tree.SelectedItems.Add(item);
                item.BringIntoView();
            }
        syncing = false;
    }

    TreeViewItem Item(XElement node)
    {
        var id = (string)node.Attribute("id")!;
        var type = (string?)node.Attribute("type") ?? "";
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Background = Avalonia.Media.Brushes.Transparent, // the whole row takes the pointer (drag, drop position)
            Children =
            {
                Icons.Node(type, 14),
                new TextBlock { Text = id },
                new TextBlock { Text = type == "Instance" ? (string?)node.Attribute("component") : Icons.Name(type), Opacity = 0.5, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var item = new TreeViewItem { Header = header, Tag = id, IsExpanded = !collapsed.Contains(id) };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property != TreeViewItem.IsExpandedProperty) return;
            if (item.IsExpanded) collapsed.Remove(id); else collapsed.Add(id);
        };
        if (type != "Instance") item.ItemsSource = node.Elements("Node").Select(child => { parentOf[(string)child.Attribute("id")!] = id; return Item(child); }).ToList();
        items[id] = item;

        // Drag a row: move the node (not the root).
        PointerPressedEventArgs? pressed = null;
        header.PointerPressed += (_, e) => pressed = node.Parent?.Name == "Node" && e.GetCurrentPoint(header).Properties.IsLeftButtonPressed ? e : null;
        header.PointerMoved += async (_, e) =>
        {
            if (pressed is not { } start || Point.Distance(start.GetPosition(header), e.GetPosition(header)) < 6) return;
            pressed = null;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(NodeFormat, id));
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
        };
        header.PointerReleased += (_, _) => pressed = null;
        return item;
    }

    /// <summary>
    /// Where a dragged row lands: the upper quarter of a row goes before it, the lower quarter after it, the middle of
    /// a container into it (at the end). Null for no valid target (the root's siblings, a node into itself).
    /// </summary>
    (string Parent, int Index)? Target(DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(NodeFormat) is not { } moving || CanvasView.CurrentGraph() is not { } graph) return null;
        if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not { Tag: string id, Header: Control header }
            || CanvasEdit.Find(graph, id) is not { } node || CanvasEdit.Find(graph, moving) is not { } moved || node.AncestorsAndSelf().Contains(moved)) return null;
        var y = e.GetPosition(header).Y / Math.Max(header.Bounds.Height, 1);
        var isContainer = ((string?)node.Attribute("type"))?.StartsWith("Container.") == true;
        if (node.Parent is not XElement { Name.LocalName: "Node" } parent || isContainer && y is > 0.25 and < 0.75)
            return isContainer ? (id, node.Elements("Node").Count(n => n != moved)) : null;
        var siblings = parent.Elements("Node").Where(n => n != moved).ToList();
        return ((string)parent.Attribute("id")!, siblings.IndexOf(node) + (y >= 0.5 ? 1 : 0));
    }
}
