using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Xml.Linq;
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
    readonly Canvas overlay = new() { IsHitTestVisible = false };
    static Dictionary<string, Control> byId = [];
    Dictionary<Control, string> idOf = [];

    /// <summary>The screen shown on the canvas.</summary>
    public static string? CurrentScreen { get; private set; }

    /// <summary>The control built for a node of the shown screen (for binding candidates in the inspector).</summary>
    public static Control? ControlOf(string id) => byId.GetValueOrDefault(id);

    static event Action<string>? ShowScreenRequested;

    /// <summary>Shows a screen on the canvas (from the explorer).</summary>
    public static void ShowScreen(string id) => ShowScreenRequested?.Invoke(id);

    /// <summary>Runs a canvas edit on the shown screen and commits the changed documents as one UI history step.</summary>
    public static void Edit(string label, Func<FaroProject, XDocument, IEnumerable<XDocument>> change)
    {
        if (Workspace.Project is not { } project || CurrentScreen is null || project.Graph(CurrentScreen) is not { } screen) return;
        UiHistory.Commit(label, change(project, screen).ToList());
        Workspace.Reload(); // show it now rather than when the file watcher fires
    }

    public static void AddNode(string type, string? component = null)
    {
        // ponytail: blocks direct self-instancing only; deeper cycles (A in B in A) are left to the snapshots
        if (component is not null && component == CurrentScreen) return;
        AddNodeTo(type, component);
    }

    static void AddNodeTo(string type, string? component) => Edit($"Add {(component ?? type).Split('.')[^1]}", (project, screen) =>
    {
        var node = CanvasEdit.Add(project, screen, Selection.Count == 1 ? Selection.First() : null, type, component);
        Selection.Clear();
        Selection.Add((string)node.Attribute("id")!);
        return [screen];
    });

    static CanvasEdit.Clip? clipboard;

    /// <summary>An Assets/ image path dragged from the explorer (in-process drag and drop).</summary>
    public static readonly DataFormat<string> AssetFormat = DataFormat.CreateInProcessFormat<string>("faro-asset");

    public static void CopySelection()
    {
        if (CurrentGraph() is { } graph && Workspace.Project is { } project && Selection.Count > 0) clipboard = CanvasEdit.Copy(project, graph, Selection);
    }

    public static void PasteClipboard() => Paste(clipboard, after: false);

    /// <summary>Duplicate: copy and paste right after the selection, in one undo step.</summary>
    public static void DuplicateSelection()
    {
        if (CurrentGraph() is { } graph && Workspace.Project is { } project && Selection.Count > 0) Paste(CanvasEdit.Copy(project, graph, Selection), after: true);
    }

    static void Paste(CanvasEdit.Clip? clip, bool after)
    {
        // A component master can't contain an instance of itself (as in AddNode).
        if (clip is null || clip.Nodes.Any(n => n.DescendantsAndSelf("Node").Any(d => (string?)d.Attribute("component") == CurrentScreen))) return;
        var anchor = after ? Selection.OrderBy(id => ScreenNodeIds.IndexOf(id)).LastOrDefault() : Selection.Count == 1 ? Selection.First() : null;
        Edit(after ? "Duplicate" : "Paste", (project, screen) =>
        {
            var (added, changed) = CanvasEdit.Paste(project, screen, anchor, clip, after);
            Selection.Clear();
            Selection.UnionWith(added.Select(n => (string)n.Attribute("id")!));
            return changed;
        });
    }

    public static void DeleteSelection() => Edit("Delete", (project, screen) => CanvasEdit.Delete(project, screen, Selection.ToList()));

    public static void MoveSelection(int delta) => Edit(delta < 0 ? "Move up" : "Move down", (_, screen) =>
        Selection.Count == 1 && CanvasEdit.Move(screen, Selection.First(), delta) ? [screen] : []);

    /// <summary>Selected node ids on the current screen (the Select menu drives this too).</summary>
    public static HashSet<string> Selection { get; } = [];
    /// <summary>Selectable node ids of the screen on the canvas (nodes inside component instances excluded).</summary>
    public static List<string> ScreenNodeIds { get; private set; } = [];
    public static event Action? SelectionChanged;

    public static void Select(IEnumerable<string> ids)
    {
        Selection.Clear();
        Selection.UnionWith(ids);
        SelectionChanged?.Invoke();
    }

    static readonly Dictionary<string, (double Width, double Height)> Sizes = new()
    {
        ["Phone"] = (390, 844),
        ["Tablet"] = (820, 1180),
        ["Desktop"] = (1280, 800),
    };

    void ApplySize()
    {
        var (width, height) = Sizes.GetValueOrDefault(FaroSettings.Current.ArtboardSize, Sizes["Phone"]);
        (artboard.Width, artboard.MinHeight) = (width, height);
        Dispatcher.UIThread.Post(DrawSelection, DispatcherPriority.Loaded); // selection boxes follow the new layout
    }

    readonly Border artboard = new() { Background = Brushes.White, [TextElement.ForegroundProperty] = Brushes.Black, Margin = new(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };

    readonly Button run = new() { Content = L.T("Run") };

    public CanvasView()
    {
        Focusable = true;
        run.Click += (_, _) => ConsoleView.RunOrStop();
        sync.Click += (_, _) => SyncComponents();
        screens.SelectionChanged += (_, _) =>
        {
            // Refreshing the list after a reload also lands here: keep the selection unless the screen really changed.
            if (screens.SelectedItem is not string screen) return;
            if (screen != CurrentScreen) { CurrentScreen = screen; Selection.Clear(); }
            Render();
            SelectionChanged?.Invoke();
        };

        var add = new Button { Content = L.T("+ Add") };
        add.Click += (_, _) =>
        {
            var menu = new MenuFlyout();
            foreach (var type in CanvasEdit.AddableTypes)
            {
                var item = new MenuItem { Header = type.Split('.')[^1] + (type.StartsWith("Container.") ? L.T(" (container)") : "") };
                item.Click += (_, _) => AddNode(type);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            foreach (var component in Workspace.Project?.Components.Keys.Where(c => c != CurrentScreen).Order() ?? Enumerable.Empty<string>())
            {
                var item = new MenuItem { Header = component };
                item.Click += (_, _) => AddNode("Instance", component);
                menu.Items.Add(item);
            }
            menu.ShowAt(add);
        };
        var delete = new Button { Content = L.T("Delete"), [ToolTip.TipProperty] = "Delete the selected nodes (Del)" };
        delete.Click += (_, _) => DeleteSelection();
        var up = new Button { Content = L.T("↑"), [ToolTip.TipProperty] = "Move up (Alt+Up)" };
        up.Click += (_, _) => MoveSelection(-1);
        var down = new Button { Content = L.T("↓"), [ToolTip.TipProperty] = "Move down (Alt+Down)" };
        down.Click += (_, _) => MoveSelection(+1);
        // Design mode: clicks select nodes instead of operating the controls. Shift adds/removes.
        artboard.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var id = NodeAt(e.GetPosition(overlay), new HashSet<string?>()); // by bounds: text without a background isn't hit-testable itself
            if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) Select(id is null ? [] : [id]);
            else if (id is not null) Select(Selection.Contains(id) ? Selection.Except([id]).ToList() : [.. Selection, id]);
            Focus(); // keyboard (Delete, Alt+arrows, Ctrl+Z) now goes to the canvas, not a text box elsewhere
            e.Handled = true;
            if (id is not null && id != (string?)CurrentGraph()?.Root!.Element("Node")!.Attribute("id") && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
            {
                dragId = id;
                pressAt = e.GetPosition(overlay);
                e.Pointer.Capture(artboard);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        artboard.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (dragId is null) return;
            var p = e.GetPosition(overlay);
            if (!dragging && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) < 5) return; // a click, not a drag yet
            dragging = true;
            artboard.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.DragMove);
            drop = DropTarget(p);
            DrawSelection();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        artboard.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            var (id, target) = (dragId, dragging ? drop : null);
            dragId = null;
            dragging = false;
            drop = null;
            artboard.Cursor = null;
            e.Pointer.Capture(null);
            if (id is not null && target is { } t) Edit("Move", (_, screen) => CanvasEdit.MoveTo(screen, id, t.Parent, t.Index) ? [screen] : []);
            else DrawSelection();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        // Images dropped from the explorer become Image nodes (in the container under the pointer, or after the node there).
        DragDrop.SetAllowDrop(artboard, true);
        artboard.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(AssetFormat) ? DragDropEffects.Copy : DragDropEffects.None);
        artboard.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.DataTransfer.TryGetValue(AssetFormat) is not { } asset) return;
            var at = NodeAt(e.GetPosition(artboard), new HashSet<string?>());
            Edit("Add image", (project, screen) =>
            {
                var node = CanvasEdit.Add(project, screen, at, "Control.Image");
                CanvasEdit.SetProp(node, "Source", asset);
                Selection.Clear();
                Selection.Add((string)node.Attribute("id")!);
                return [screen];
            });
        });

        // Preview sizes: check how Fill / Grid / Overlay layouts stretch on other devices.
        var size = new ComboBox { ItemsSource = Sizes.Keys, SelectedItem = Sizes.ContainsKey(FaroSettings.Current.ArtboardSize) ? FaroSettings.Current.ArtboardSize : "Phone" };
        size.SelectionChanged += (_, _) =>
        {
            FaroSettings.Current.ArtboardSize = (string)size.SelectedItem!;
            FaroSettings.Current.Save();
            ApplySize();
        };
        ApplySize();
        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new(8), Children = { screens, size, add, delete, up, down, sync, run, status } };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { bar, new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = artboard } } } };
    }

    void ShowRunState()
    {
        run.Content = L.T(Workspace.Running ? "Stop" : "Run");
        run.IsEnabled = Workspace.Trusted;
        ToolTip.SetTip(run, Workspace.Trusted ? null : L.T("Restricted Mode: File › Trust Project… to run it."));
        if (ScriptPreview.Outdated(Workspace.Root)) Render(); // a new build: redraw Script nodes
        DrawSelection(); // refreshes the status line (unbuilt changes)
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Refresh;
        Workspace.RunChanged += ShowRunState;
        SelectionChanged += DrawSelection;
        ShowScreenRequested += ShowScreenHere;
        Refresh();
        ShowRunState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Refresh;
        Workspace.RunChanged -= ShowRunState;
        SelectionChanged -= DrawSelection;
        ShowScreenRequested -= ShowScreenHere;
        base.OnDetachedFromVisualTree(e);
    }

    void ShowScreenHere(string id)
    {
        Refresh();
        screens.SelectedItem = id;
    }

    void Refresh()
    {
        var selected = screens.SelectedItem as string;
        // Screens first, then component masters (edited here too; instances pick changes up via Sync components).
        screens.ItemsSource = Workspace.Project is { } p0 ? [.. p0.Screens.Keys.Order(), .. p0.Components.Keys.Order()] : new List<string>();
        screens.SelectedItem = selected is not null && Workspace.Project?.Graph(selected) is not null ? selected : Workspace.Project?.Screens.Keys.Order().FirstOrDefault();
        var outOfDate = Workspace.Project is { } p ? ComponentSync.OutOfDate(p).Count : 0;
        sync.Content = L.F("Sync components ({0})", outOfDate);
        sync.IsEnabled = outOfDate > 0;
        Render();
    }

    void Render()
    {
        if (Workspace.Project?.Graph(screens.SelectedItem as string ?? "") is not { } graph)
        {
            artboard.Child = new TextBlock { Text = L.F("No UI/*.xml screens in {0}", Workspace.Root), Margin = new(16), Foreground = Brushes.Gray };
            return;
        }
        byId = [];
        var built = UiBuilder.Build(MockData.Expand(graph.Root!.Element("Node")!), byId, Workspace.Root);
        // An instance selects as a whole; mock row copies ("id~2") aren't nodes of the file.
        idOf = byId.Where(p => !p.Key.Contains('/') && !p.Key.Contains('~')).ToDictionary(p => p.Value, p => p.Key);
        ScreenNodeIds = [.. idOf.Values];
        Selection.IntersectWith(ScreenNodeIds);
        (overlay.Parent as Panel)?.Children.Remove(overlay);
        artboard.Child = new Panel { Children = { built, overlay } };
        Dispatcher.UIThread.Post(DrawSelection, DispatcherPriority.Loaded); // after layout, so bounds are known
        foreach (var group in Workspace.Issues.Where(i => i.Screen == CurrentScreen).GroupBy(i => i.NodeId))
            if (byId.TryGetValue(group.Key, out var control))
            {
                var badge = Badge(group);
                // Clicking opens the re-binding panel (spec §6): the node's bindings in the inspector, with candidates.
                var nodeId = group.Key.Split('/')[0]; // a node inside an instance selects the instance
                if (ScreenNodeIds.Contains(nodeId))
                {
                    badge.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                    badge.PointerPressed += (_, e) => { Select([nodeId]); e.Handled = true; };
                    ToolTip.SetTip(badge, ToolTip.GetTip(badge) + "\n\nClick to fix it in the inspector.");
                }
                AdornerLayer.SetIsClipEnabled(badge, false);
                AdornerLayer.SetAdorner(control, badge);
            }
    }

    public static void SyncComponents()
    {
        if (Workspace.Project is { } p) UiHistory.Commit("Sync components", ComponentSync.Sync(p));
    }

    string? dragId;
    Point pressAt;
    bool dragging;
    (string Parent, int Index, Rect Line)? drop;

    /// <summary>The innermost node whose bounds contain the point.</summary>
    string? NodeAt(Point p, IReadOnlySet<string?> excluded) =>
        idOf.Where(c => !excluded.Contains(c.Value)).Select(c => (Id: c.Value, Rect: RectOf(c.Key)))
            .Where(h => h.Rect.Contains(p)).OrderBy(h => h.Rect.Width * h.Rect.Height).Select(h => h.Id).FirstOrDefault();

    static XDocument? CurrentGraph() => CurrentScreen is null ? null : Workspace.Project?.Graph(CurrentScreen);

    Rect RectOf(Control c) => c.TranslatePoint(default, overlay) is { } p ? new Rect(p, c.Bounds.Size) : default;

    /// <summary>
    /// Where a drag would drop (Figma Auto Layout style): into the innermost container under the pointer, or
    /// beside the node under it, at the child position the pointer is at; plus the insertion line to draw.
    /// </summary>
    (string Parent, int Index, Rect Line)? DropTarget(Point p)
    {
        if (CurrentGraph() is not { } graph || CanvasEdit.Find(graph, dragId!) is not { } dragged) return null;
        var excluded = dragged.DescendantsAndSelf("Node").Select(n => (string?)n.Attribute("id")).ToHashSet();
        var hit = NodeAt(p, excluded) is { } hitId ? CanvasEdit.Find(graph, hitId) : null;
        var container = hit is null ? graph.Root!.Element("Node")! : CanvasEdit.IsContainer(hit) ? hit : hit.Parent!;
        if (!CanvasEdit.IsContainer(container) || excluded.Contains((string?)container.Attribute("id"))) return null;

        var containerBox = RectOf(byId[(string)container.Attribute("id")!]);
        if ((string?)container.Attribute("type") == "Container.Overlay") // children overlap: a drop goes on top (last in z-order)
            return ((string)container.Attribute("id")!, container.Elements("Node").Count(n => n != dragged), new Rect(containerBox.X + 4, containerBox.Y + 4, containerBox.Width - 8, 2));
        var horizontal = (string?)container.Attribute("direction") == "Horizontal" && (string?)container.Attribute("type") == "Container.Stack";
        var reading = (string?)container.Attribute("type") != "Container.Stack"; // wrap / grid: rows, then left to right
        var children = container.Elements("Node").Where(n => n != dragged).Select(n => RectOf(byId[(string)n.Attribute("id")!])).ToList();
        var index = children.Count(r => horizontal ? r.Center.X < p.X
            : reading ? p.Y > r.Bottom || (p.Y >= r.Top && p.X > r.Center.X)
            : r.Center.Y < p.Y);
        var box = RectOf(byId[(string)container.Attribute("id")!]);
        var vertical = horizontal || reading; // a vertical insertion line between items laid out side by side
        Rect line = children.Count == 0 ? new Rect(box.X + 4, box.Y + 4, box.Width - 8, 2)
            : index < children.Count
                ? vertical ? new Rect(children[index].X - 2, children[index].Y, 2, children[index].Height) : new Rect(children[index].X, children[index].Y - 2, children[index].Width, 2)
                : vertical ? new Rect(children[^1].Right + 1, children[^1].Y, 2, children[^1].Height) : new Rect(children[^1].X, children[^1].Bottom + 1, children[^1].Width, 2);
        return ((string)container.Attribute("id")!, index, line);
    }

    void DrawSelection()
    {
        overlay.Children.Clear();
        if (drop is { } d)
        {
            var marker = new Avalonia.Controls.Shapes.Rectangle { Width = d.Line.Width, Height = d.Line.Height, Fill = new SolidColorBrush(Color.Parse("#1473E6")) };
            Canvas.SetLeft(marker, d.Line.X);
            Canvas.SetTop(marker, d.Line.Y);
            overlay.Children.Add(marker);
        }
        foreach (var id in Selection)
            if (byId.GetValueOrDefault(id) is { } c && c.TranslatePoint(default, overlay) is { } p)
            {
                var box = new Avalonia.Controls.Shapes.Rectangle { Width = c.Bounds.Width, Height = c.Bounds.Height, Stroke = new SolidColorBrush(Color.Parse("#1473E6")), StrokeThickness = 2 };
                Canvas.SetLeft(box, p.X);
                Canvas.SetTop(box, p.Y);
                overlay.Children.Add(box);
            }
        var selected = Selection.Count == 1 && CurrentGraph() is { } graph
            ? graph.Descendants("Node").FirstOrDefault(n => (string?)n.Attribute("id") == Selection.First())
            : null;
        var editingComponent = CurrentScreen is not null && Workspace.Project?.Components.ContainsKey(CurrentScreen) == true
            ? L.F("Component master · {0} instance(s) to sync · ", ComponentSync.OutOfDate(Workspace.Project!).Count(n => (string?)n.Attribute("component") == CurrentScreen)) : "";
        status.Text = Workspace.LoadError
            ?? (selected is not null ? L.F("Selected: {0} ({1}) · ", Selection.First(), (string?)selected.Attribute("type")) : Selection.Count > 1 ? L.F("{0} nodes selected · ", Selection.Count) : "")
            + editingComponent + L.F("{0} broken binding(s) · {1} registry members", Workspace.Issues.Count, Workspace.Registry.Count)
            + (Workspace.Unbuilt ? L.T(" · Unbuilt code changes: new members resolve after Run") : "") // spec §11.5
            + (Workspace.Trusted ? "" : L.T(" · Restricted Mode (File › Trust Project…)"));
    }

    /// <summary>
    /// The chat request for a binding whose target member is missing, with the context the canvas knows:
    /// node, event or bound property and its type (spec §8 round trip). Null for other kinds of issue.
    /// </summary>
    public static string? VibeRequest(BindingIssue issue, Control control)
    {
        var bind = Workspace.Project?.BindsFor(CurrentScreen ?? "").FirstOrDefault(b => (string?)b.Attribute("nodeId") == issue.NodeId && (string?)b.Attribute("target") == issue.Target);
        if (bind is null || issue.Target.Length == 0 || issue.Target.StartsWith("Navigate:") || Workspace.Registry.Any(m => m.Target == issue.Target)) return null;
        var type = Workspace.Project!.Graph(CurrentScreen ?? "") is { } graph && CanvasEdit.FindPath(graph, issue.NodeId) is { } found ? Bindable.TypeOf(found) : null;
        var node = $"node `{issue.NodeId}` ({type})";
        if ((string?)bind.Attribute("event") is { } eventName)
            return $"Create `{issue.Target}`: a public parameterless method that runs on {eventName} of {node}.";
        var prop = (string?)bind.Attribute("prop") ?? "";
        var propType = Faro.Runtime.Bindable.For(control)?.Props.GetValueOrDefault(prop) is { } p ? Faro.Runtime.Bindable.ValueType(p).Name : "object";
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
            i.Message + (i.Suggestions.Count > 0 ? "\n" + L.T("Did you mean:") + " " + string.Join(", ", i.Suggestions) : ""))),
    };
}
