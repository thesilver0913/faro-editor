using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Xml.Linq;
using Faro.Runtime;
using FluentAvalonia.UI.Controls;

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

    /// <summary>Node types and components for "+ Add" and the context menu's Add (into the selected container, or after the node).</summary>
    static List<MenuItem> AddItems()
    {
        var items = new List<MenuItem>();
        foreach (var type in CanvasEdit.AddableTypes)
        {
            var item = new MenuItem { Header = type.Split('.')[^1] + (type.StartsWith("Container.") ? L.T(" (container)") : "") };
            item.Click += (_, _) => AddNode(type);
            items.Add(item);
        }
        foreach (var component in Workspace.Project?.Components.Keys.Where(c => c != CurrentScreen).Order() ?? Enumerable.Empty<string>())
        {
            var item = new MenuItem { Header = component };
            item.Click += (_, _) => AddNode("Instance", component);
            items.Add(item);
        }
        return items;
    }

    static CanvasView? instance;

    /// <summary>The canvas menu at the pointer over another control (the layers panel's right-click).</summary>
    public static void ShowMenu(Control at) => instance?.Menu().ShowAt(at, showAtPointer: true);

    /// <summary>Rename… for the selected node (F2 in the layers panel).</summary>
    public static void RenameSelected() => instance?.RenameSelection();

    /// <summary>The canvas right-click menu, on the selection.</summary>
    MenuFlyout Menu()
    {
        var any = Selection.Count > 0;
        var single = Selection.Count == 1 && CurrentGraph() is { } g ? CanvasEdit.Find(g, Selection.First()) : null;
        var isRoot = single is not null && single.Parent?.Name != "Node";
        MenuItem Item(string header, string? gesture, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = L.T(header), InputGesture = gesture is null ? null : Avalonia.Input.KeyGesture.Parse(gesture), IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        var addMenu = new MenuItem { Header = L.T("Add") };
        foreach (var item in AddItems()) addMenu.Items.Add(item);
        var wrap = new MenuItem { Header = L.T("Wrap in"), IsEnabled = any && !isRoot };
        foreach (var type in new[] { "Container.Stack", "Container.Overlay", "Container.Grid" })
            wrap.Items.Add(Item(type.Split('.')[^1], null, () => WrapSelection(type)));
        var menu = new MenuFlyout
        {
            Items =
            {
                addMenu,
                new Separator(),
                Item("Cut", "Ctrl+X", CutSelection, any && !isRoot),
                Item("Copy", "Ctrl+C", CopySelection, any),
                Item("Paste", "Ctrl+V", PasteClipboard, clipboard is not null),
                Item("Duplicate", "Ctrl+D", DuplicateSelection, any && !isRoot),
                Item("Delete", "Delete", DeleteSelection, any && !isRoot),
                new Separator(),
                Item("Move up", "Alt+Up", () => MoveSelection(-1), single is not null && !isRoot),
                Item("Move down", "Alt+Down", () => MoveSelection(+1), single is not null && !isRoot),
                Item("Select parent", null, () => Select([(string)single!.Parent!.Attribute("id")!]), single is not null && !isRoot),
                wrap,
                Item("Rename…", null, RenameSelection, single is not null),
            },
        };
        if ((string?)single?.Attribute("type") == "Instance")
            menu.Items.Add(Item("Edit master component", null, () => ShowScreen((string)single!.Attribute("component")!)));
        return menu;
    }

    public static void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    static void WrapSelection(string type) => Edit(L.T("Wrap in") + " " + type.Split('.')[^1], (_, screen) =>
    {
        if (CanvasEdit.Wrap(screen, Selection, type) is not { } container) return [];
        Select([(string)container.Attribute("id")!]);
        return [screen];
    });

    async void RenameSelection()
    {
        if (Selection.Count != 1 || TopLevel.GetTopLevel(this) is not Window owner) return;
        var id = Selection.First();
        if (await Dialogs.Prompt(owner, L.T("Rename"), L.T("New ID (references follow):"), id) is not { Length: > 0 } newId) return;
        string? error = null;
        Edit("Rename node", (project, screen) =>
        {
            var (err, changed) = CanvasEdit.Rename(project, screen, id, newId);
            error = err;
            if (err is null && changed.Count > 0) Select([newId.Trim()]);
            return changed;
        });
        if (error is not null) await Dialogs.Info(owner, L.T("Rename"), new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
    }

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

    readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly LayoutTransformControl zoomHost = new();
    readonly TextBlock zoomLabel = new() { Text = "100%", VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
    double zoom = 1;

    void SetZoom(double value)
    {
        zoom = Math.Clamp(value, 0.25, 4);
        zoomHost.LayoutTransform = new ScaleTransform(zoom, zoom);
        zoomLabel.Text = $"{Math.Round(zoom * 100)}%";
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

    readonly Border artboard = new() { Margin = new(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Classes = { "faro-screen" } };
    readonly ThemeVariantScope designScope = new() { RequestedThemeVariant = ThemeVariant.Light };
    AppDesign? shownDesign;

    /// <summary>The artboard shows the app's design language (faro.json "design"); "System" previews light.</summary>
    void ShowDesign(AppDesign design)
    {
        if (design == shownDesign) return;
        shownDesign = design;
        designScope.Styles.Clear();
        designScope.Resources = new ResourceDictionary();
        design.Apply(designScope.Styles, designScope.Resources);
        designScope.RequestedThemeVariant = design.Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        if (design.Language == "Fluent")
        {
            artboard.Background = design.Theme == "Dark" ? new SolidColorBrush(Color.Parse("#202020")) : Brushes.White;
            artboard[TextElement.ForegroundProperty] = design.Theme == "Dark" ? Brushes.White : Brushes.Black;
        }
        else
        {
            artboard.ClearValue(Border.BackgroundProperty); // the language's surface (Border.faro-screen)
            artboard.ClearValue(TextElement.ForegroundProperty);
        }
    }

    readonly Button run = new() { Classes = { "accent" }, Padding = new(8, 4), [ToolTip.TipProperty] = L.T("Run the app (F5) / Stop (Shift+F5)") };

    public CanvasView()
    {
        instance = this;
        Focusable = true;
        zoomHost.Child = artboard;
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

        Button? add = null;
        add = Icons.Button(FASymbol.Add, "Add a node (or drag one from Parts)", () =>
        {
            var menu = new MenuFlyout();
            foreach (var item in AddItems()) menu.Items.Add(item);
            menu.ShowAt(add!);
        });
        var delete = Icons.Button(FASymbol.Delete, "Delete the selected nodes (Del)", DeleteSelection);
        var up = Icons.Button(FASymbol.ChevronUp, "Move up (Alt+Up)", () => MoveSelection(-1));
        var down = Icons.Button(FASymbol.ChevronDown, "Move down (Alt+Down)", () => MoveSelection(+1));
        // Design mode: clicks select nodes instead of operating the controls. Shift adds/removes.
        artboard.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (preview) return; // the controls themselves take the clicks
            // The selection's handles (right edge, bottom edge, corner) resize it to a fixed width / height.
            if (e.GetCurrentPoint(artboard).Properties.IsLeftButtonPressed && Handle(e.GetPosition(overlay)) is { } axes && byId.GetValueOrDefault(Selection.First()) is { } resized)
            {
                (resizeId, resizeAxes, resizeFrom, pressAt) = (Selection.First(), axes, resized.Bounds.Size, e.GetPosition(overlay));
                e.Pointer.Capture(artboard);
                e.Handled = true;
                return;
            }
            var id = NodeAt(e.GetPosition(overlay), new HashSet<string?>()); // by bounds: text without a background isn't hit-testable itself
            if (e.GetCurrentPoint(artboard).Properties.IsRightButtonPressed)
            {
                // Right-click: act on the node under the pointer (keeping a multi-selection that includes it).
                if (id is null || !Selection.Contains(id)) Select(id is null ? [] : [id]);
                Focus();
                Menu().ShowAt(artboard, showAtPointer: true);
                e.Handled = true;
                return;
            }
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
            var p = e.GetPosition(overlay);
            if (resizeId is not null && byId.GetValueOrDefault(resizeId) is { } resizing)
            {
                if (resizeAxes.W) resizing.Width = Math.Max(8, Math.Round(resizeFrom.Width + p.X - pressAt.X));
                if (resizeAxes.H) resizing.Height = Math.Max(8, Math.Round(resizeFrom.Height + p.Y - pressAt.Y));
                Dispatcher.UIThread.Post(DrawSelection, DispatcherPriority.Loaded);
                return;
            }
            if (dragId is null)
            {
                artboard.Cursor = preview ? null : Handle(p) switch
                {
                    (true, true) => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.BottomRightCorner),
                    (true, false) => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast),
                    (false, true) => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth),
                    _ => null,
                };
                return;
            }
            if (!dragging && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) < 5) return; // a click, not a drag yet
            dragging = true;
            artboard.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.DragMove);
            drop = DropTarget(p);
            DrawSelection();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        artboard.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (preview) return; // don't take the pointer capture away from a pressed control
            if (resizeId is { } rid && byId.GetValueOrDefault(rid) is { } sized)
            {
                resizeId = null;
                e.Pointer.Capture(null);
                var (w, h, axes) = (sized.Width, sized.Height, resizeAxes);
                Edit("Resize", (_, screen) =>
                {
                    if (CanvasEdit.Find(screen, rid) is not { } n) return [];
                    if (axes.W) { CanvasEdit.SetAttribute(n, "widthSizing", "Fixed"); CanvasEdit.SetAttribute(n, "width", w.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                    if (axes.H) { CanvasEdit.SetAttribute(n, "heightSizing", "Fixed"); CanvasEdit.SetAttribute(n, "height", h.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                    return [screen];
                });
                return;
            }
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
        // Parts from the palette are added the same way.
        artboard.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(AssetFormat) || e.DataTransfer.Contains(PaletteView.PartFormat) ? DragDropEffects.Copy : DragDropEffects.None);
        artboard.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var at = NodeAt(e.GetPosition(artboard), new HashSet<string?>());
            if (e.DataTransfer.TryGetValue(PaletteView.PartFormat) is { } part)
            {
                Select(at is null ? [] : [at]);
                PaletteView.Add(part);
                return;
            }
            if (e.DataTransfer.TryGetValue(AssetFormat) is not { } asset) return;
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
        // Zoom: −/+, Fit (to the pane width), Ctrl+wheel. The artboard's own coordinates don't change, so selection and drops still line up.
        var zoomOut = Icons.Button(FASymbol.ZoomOut, "Zoom out (Ctrl+wheel)", () => SetZoom(zoom / 1.25));
        var zoomIn = Icons.Button(FASymbol.ZoomIn, "Zoom in (Ctrl+wheel)", () => SetZoom(zoom * 1.25));
        var fit = Icons.Button(FASymbol.FullScreenMaximize, "Fit to the pane width", () => SetZoom(Math.Min(1, (viewport.Bounds.Width - 24) / (artboard.Width + artboard.Margin.Left + artboard.Margin.Right))));
        zoomLabel.PointerPressed += (_, _) => SetZoom(1); // click the percentage: back to 100%
        viewport.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            SetZoom(zoom * Math.Pow(1.1, e.Delta.Y));
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        var zoomBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { zoomOut, zoomLabel, zoomIn, fit } };
        // Preview (Figma's prototype mode): try the screen with its mock data — type, click, and follow Navigate bindings.
        var previewToggle = new Avalonia.Controls.Primitives.ToggleButton { Content = Icons.Label(FASymbol.SlideShow, L.T("Preview")), Padding = new(8, 4), [ToolTip.TipProperty] = L.T("Try the screen here: typing, clicks and Navigate bindings work; code isn't run (Run does that)") };
        previewToggle.IsCheckedChanged += (_, _) =>
        {
            preview = previewToggle.IsChecked == true;
            if (preview) Select([]);
            Render();
        };
        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new(8), Children = { screens, size, zoomBar, add, delete, up, down, sync, previewToggle, run, status } };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        designScope.Child = zoomHost;
        viewport.Content = designScope;
        Content = new DockPanel { Children = { bar, viewport } };
    }

    void ShowRunState()
    {
        run.Content = Icons.Label(Workspace.Running ? FASymbol.Stop : FASymbol.Play, L.T(Workspace.Running ? "Stop" : "Run"));
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
        sync.Content = Icons.Label(FASymbol.Sync, outOfDate.ToString());
        ToolTip.SetTip(sync, L.F("Sync components ({0})", outOfDate));
        sync.IsEnabled = outOfDate > 0;
        Render();
    }

    static bool preview;

    /// <summary>In preview, Click bindings to Navigate:… switch the canvas to that screen.</summary>
    void WirePreview(string screenId)
    {
        foreach (var bind in Workspace.Project!.BindsFor(screenId).Where(b => (string?)b.Attribute("event") == "Click" && ((string?)b.Attribute("target"))?.StartsWith("Navigate:") == true))
            if (byId.GetValueOrDefault((string?)bind.Attribute("nodeId") ?? "") is Button button)
            {
                var to = FaroApp.NavigateScreenId((string)bind.Attribute("target")!, Workspace.Project.Screens.Keys);
                button.Click += (_, _) => { if (Workspace.Project.Screens.ContainsKey(to)) screens.SelectedItem = to; };
            }
    }

    void Render()
    {
        if (Workspace.Project?.Graph(screens.SelectedItem as string ?? "") is not { } graph)
        {
            artboard.Child = new TextBlock { Text = L.F("No UI/*.xml screens in {0}", Workspace.Root), Margin = new(16), Foreground = Brushes.Gray };
            return;
        }
        ShowDesign(Workspace.Project.Design);
        byId = [];
        var built = UiBuilder.Build(MockData.Expand(graph.Root!.Element("Node")!), byId, Workspace.Root);
        // An instance selects as a whole; mock row copies ("id~2") aren't nodes of the file.
        idOf = byId.Where(p => !p.Key.Contains('/') && !p.Key.Contains('~')).ToDictionary(p => p.Value, p => p.Key);
        ScreenNodeIds = [.. idOf.Values];
        Selection.IntersectWith(ScreenNodeIds);
        (overlay.Parent as Panel)?.Children.Remove(overlay);
        artboard.Child = new Panel { Children = { built, overlay } };
        if (preview) WirePreview(CurrentScreen ?? "");
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

    public static XDocument? CurrentGraph() => CurrentScreen is null ? null : Workspace.Project?.Graph(CurrentScreen);

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

    string? resizeId;
    (bool W, bool H) resizeAxes;
    Size resizeFrom;

    /// <summary>The single selection's box on the overlay (null for none or several).</summary>
    Rect? SelectedBox() => Selection.Count == 1 && byId.GetValueOrDefault(Selection.First()) is { } c && c.TranslatePoint(default, overlay) is { } p
        ? new Rect(p, c.Bounds.Size) : null;

    /// <summary>Which resize handle is under the pointer: the right edge (width), the bottom edge (height) or the corner (both).</summary>
    (bool W, bool H)? Handle(Point p)
    {
        if (SelectedBox() is not { } box || !box.Inflate(6).Contains(p)) return null;
        var (w, h) = (Math.Abs(p.X - box.Right) <= 6, Math.Abs(p.Y - box.Bottom) <= 6);
        return w || h ? (w, h) : null;
    }

    void DrawSelection()
    {
        overlay.Children.Clear();
        overlay.IsVisible = !preview;
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
        if (SelectedBox() is { } sel) // resize handles
            foreach (var (x, y) in new[] { (sel.Right, sel.Center.Y), (sel.Center.X, sel.Bottom), (sel.Right, sel.Bottom) })
            {
                var handle = new Avalonia.Controls.Shapes.Rectangle { Width = 8, Height = 8, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.Parse("#1473E6")), StrokeThickness = 1.5 };
                Canvas.SetLeft(handle, x - 4);
                Canvas.SetTop(handle, y - 4);
                overlay.Children.Add(handle);
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
