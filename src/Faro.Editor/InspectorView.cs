using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// Inspector (Unity-style, spec §1): edits the selected node's id, layout and props, and its bindings
/// with registry candidates (spec §6). Every change is one UI history step.
/// </summary>
public sealed class InspectorView : UserControl
{
    static readonly Dictionary<string, string[]> PropsByType = new()
    {
        ["Control.Button"] = ["Text", "BackgroundTexture"],
        ["Control.TextInput"] = ["Placeholder", "Text"],
        ["Control.Text"] = ["Text"],
        ["Control.Image"] = ["Source"],
    };

    readonly StackPanel body = new() { Spacing = 8, Margin = new(12) };

    public InspectorView() => Content = new ScrollViewer { Content = body };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CanvasView.SelectionChanged += Render;
        Workspace.Changed += RenderUnlessTyping;
        Render();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CanvasView.SelectionChanged -= Render;
        Workspace.Changed -= RenderUnlessTyping;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>A reload caused elsewhere (e.g. a code save) must not wipe what is being typed here.</summary>
    void RenderUnlessTyping()
    {
        if (!IsKeyboardFocusWithin) Render();
    }

    void Render()
    {
        body.Children.Clear();
        var project = Workspace.Project;
        var screen = CanvasView.CurrentScreen is { } s ? project?.Graph(s) : null;
        if (project is null || screen is null || CanvasView.Selection.Count != 1 || CanvasEdit.Find(screen, CanvasView.Selection.First()) is not { } node)
        {
            body.Children.Add(Hint(CanvasView.Selection.Count > 1 ? L.F("{0} nodes selected.", CanvasView.Selection.Count) : L.T("Select a node on the canvas.")));
            return;
        }
        var id = (string)node.Attribute("id")!;
        var type = (string?)node.Attribute("type") ?? "";

        body.Children.Add(new TextBlock { Text = type == "Instance" ? L.F("Instance of {0}", (string?)node.Attribute("component")) : type, FontWeight = FontWeight.SemiBold, FontSize = 15 });
        var idError = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(Row(L.T("ID"), Field(id, value =>
        {
            string? error = null;
            CanvasView.Edit("Rename node", (p, sc) =>
            {
                var (err, changed) = CanvasEdit.Rename(p, sc, id, value);
                error = err;
                if (err is null && changed.Count > 0) CanvasView.Select([value.Trim()]);
                return changed;
            });
            idError.Text = error ?? "";
        })));
        body.Children.Add(idError);

        body.Children.Add(Section(L.T("Layout")));
        body.Children.Add(Row(L.T("Width"), Sizing(node, "width")));
        body.Children.Add(Row(L.T("Height"), Sizing(node, "height")));
        // Placement in the parent: anchors in an Overlay, alignment and fill weight in a Stack.
        var parentType = (string?)node.Parent?.Attribute("type");
        if (parentType == "Container.Overlay")
        {
            body.Children.Add(Row(L.T("Anchor X"), Choice(node, "anchorX", ["Left", "Center", "Right"])));
            body.Children.Add(Row(L.T("Anchor Y"), Choice(node, "anchorY", ["Top", "Center", "Bottom"])));
        }
        else if (parentType == "Container.Grid")
        {
            body.Children.Add(Row(L.T("Row / Col"), Pair(AttributeField(node, "row", integer: true), AttributeField(node, "column", integer: true))));
            body.Children.Add(Row(L.T("Span R / C"), Pair(AttributeField(node, "rowSpan", integer: true), AttributeField(node, "columnSpan", integer: true))));
            body.Children.Add(Row(L.T("Align self"), Choice(node, "alignSelf", ["Auto", "Start", "Center", "End"], unset: "Auto")));
        }
        else if (parentType == "Container.Stack")
        {
            body.Children.Add(Row(L.T("Align self"), Choice(node, "alignSelf", ["Auto", "Start", "Center", "End"], unset: "Auto")));
            var mainAxis = (string?)node.Parent!.Attribute("direction") == "Horizontal" ? "width" : "height";
            if (UiBuilder.Sizing(node, mainAxis) == "Fill") body.Children.Add(Row(L.T("Fill weight"), AttributeField(node, "weight")));
        }
        body.Children.Add(Row(L.T("Min W / H"), Pair(AttributeField(node, "minWidth"), AttributeField(node, "minHeight"))));
        body.Children.Add(Row(L.T("Max W / H"), Pair(AttributeField(node, "maxWidth"), AttributeField(node, "maxHeight"))));
        body.Children.Add(Row(L.T("Margin"), AttributeField(node, "margin", sides: true)));
        if (CanvasEdit.IsContainer(node))
        {
            if (type is "Container.Stack" or "Container.Wrap") body.Children.Add(Row(L.T("Direction"), Choice(node, "direction", ["Vertical", "Horizontal"])));
            if (type == "Container.Grid")
            {
                string? TrackError(string v) => UiBuilder.Tracks(v) is null ? L.T("Tracks like \"Auto, *, 2*, 120px\", or a count like \"3\".") : null;
                body.Children.Add(Row(L.T("Columns"), CheckedField(node, "columns", 160, TrackError)));
                body.Children.Add(Row(L.T("Rows"), CheckedField(node, "rows", 160, TrackError)));
            }
            if (type != "Container.Overlay") body.Children.Add(Row(L.T("Gap"), AttributeField(node, "gap")));
            body.Children.Add(Row(L.T("Padding"), AttributeField(node, "padding", sides: true)));
            if (type != "Container.Overlay") body.Children.Add(Row(L.T("Alignment"), Choice(node, "alignment", ["Start", "Center", "End"])));
            if (type == "Container.Stack")
            {
                body.Children.Add(Row(L.T("Justify"), Choice(node, "justify", ["Start", "Center", "End", "SpaceBetween"])));
                var mainAxis = (string?)node.Attribute("direction") == "Horizontal" ? "width" : "height";
                if (node.Elements("Node").Any(c => UiBuilder.Sizing(c, mainAxis) == "Fill"))
                    body.Children.Add(Hint(L.T("Justify has no effect while a child fills the main axis: it takes the free space.")));
            }
        }

        var props = type == "Instance"
            ? node.Element("Node")?.Elements("Prop").Select(p => (string)p.Attribute("name")!).ToArray() ?? []
            : PropsByType.GetValueOrDefault(type, []);
        if (props.Length > 0)
        {
            body.Children.Add(Section(L.T(type == "Instance" ? "Overrides" : "Properties")));
            foreach (var prop in props)
                body.Children.Add(Row(prop, prop is "Source" or "BackgroundTexture"
                    ? AssetField(CanvasEdit.GetProp(node, prop) ?? "", value => EditNode(id, $"Set {prop}", n => CanvasEdit.SetProp(n, prop, value)))
                    : Field(CanvasEdit.GetProp(node, prop) ?? "", value => EditNode(id, $"Set {prop}", n => CanvasEdit.SetProp(n, prop, value)))));
        }
        if (type == "Control.Script")
        {
            // The class that builds this node in code (a FaroScript in Source/).
            body.Children.Add(Section(L.T("Script")));
            var cls = (string?)node.Attribute("class") ?? "";
            var classBox = new AutoCompleteBox { Text = cls, ItemsSource = Workspace.ScriptClasses, FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinWidth = 200, PlaceholderText = L.T("MyApp.Views.MyScript") };
            Commit(classBox, () => classBox.Text ?? "", value => EditNode(id, "Set script class", n => n.SetAttributeValue("class", value.Trim())));
            body.Children.Add(Row(L.T("Class"), classBox));
            if (cls.Length > 0 && !Workspace.ScriptClasses.Contains(cls))
            {
                var create = new Button { Content = L.T("Create with AI Chat") };
                create.Click += (_, _) => ChatView.Prefill($"Create `{cls}`: a public class deriving from Faro.Runtime.FaroScript whose Build() returns the Avalonia control for Script node `{id}` on screen {CanvasView.CurrentScreen}.");
                body.Children.Add(create);
            }
        }
        if (type == "Instance")
        {
            var repeatable = new CheckBox { Content = L.T("Repeatable (list)"), IsChecked = (string?)node.Attribute("repeatable") == "true" };
            repeatable.IsCheckedChanged += (_, _) => EditNode(id, "Set repeatable", n => CanvasEdit.SetAttribute(n, "repeatable", repeatable.IsChecked == true ? "true" : null));
            body.Children.Add(repeatable);
            if (repeatable.IsChecked == true && MockData.Fields(node) is { Count: > 0 } fields)
                body.Children.Add(MockRows(id, node, fields));
        }

        body.Children.Add(Section(L.T("Bindings")));
        // An instance also lists the bindings of its inner nodes ("orderList/price").
        foreach (var bind in project.BindsFor(CanvasView.CurrentScreen!).Where(b => (string?)b.Attribute("nodeId") is { } n && (n == id || n.StartsWith(id + "/"))).ToList())
            body.Children.Add(BindRow(bind));
        body.Children.Add(AddBindRow(id, [id, .. InnerPaths(node, id)]));
    }

    static void EditNode(string id, string label, Action<XElement> change) => CanvasView.Edit(label, (_, screen) =>
    {
        if (CanvasEdit.Find(screen, id) is not { } node) return [];
        change(node);
        return [screen];
    });

    Control Sizing(XElement node, string axis)
    {
        var id = (string)node.Attribute("id")!;
        var mode = new ComboBox { ItemsSource = new[] { "Fill", "Hug", "Fixed" }, SelectedItem = UiBuilder.Sizing(node, axis), MinWidth = 90 };
        mode.SelectionChanged += (_, _) => EditNode(id, $"Set {axis}", n => CanvasEdit.SetAttribute(n, axis + "Sizing", (string)mode.SelectedItem!));
        var size = AttributeField(node, axis);
        size.IsVisible = UiBuilder.Sizing(node, axis) == "Fixed";
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { mode, size } };
    }

    /// <param name="unset">A choice that removes the attribute (e.g. "Auto": follow the container).</param>
    Control Choice(XElement node, string attribute, string[] values, string? unset = null)
    {
        var id = (string)node.Attribute("id")!;
        var box = new ComboBox { ItemsSource = values, SelectedItem = (string?)node.Attribute(attribute) ?? values[0], MinWidth = 120 };
        box.SelectionChanged += (_, _) => EditNode(id, $"Set {attribute}", n => CanvasEdit.SetAttribute(n, attribute, (string)box.SelectedItem! == unset ? null : (string)box.SelectedItem!));
        return box;
    }

    /// <param name="sides">Padding/margin: 1, 2 or 4 numbers ("8", "8 16", "8 16 8 16": top right bottom left).</param>
    Control AttributeField(XElement node, string attribute, bool sides = false, bool integer = false) =>
        CheckedField(node, attribute, 80, value =>
        {
            var numbers = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            return numbers.Any(v => integer ? !int.TryParse(v, out var i) || i < 0 : !double.TryParse(v, out _)) ? L.T(integer ? "A whole number (0 or more)." : "Numbers only.")
                : numbers.Length > 1 && !(sides && numbers.Length is 2 or 4) ? L.T(sides ? "1, 2 or 4 numbers (top right bottom left)." : "One number.")
                : null;
        });

    /// <summary>An attribute field that rejects invalid values with the reason (red outline) instead of breaking the canvas.</summary>
    Control CheckedField(XElement node, string attribute, double width, Func<string, string?> validate)
    {
        var id = (string)node.Attribute("id")!;
        TextBox? box = null;
        box = Field((string?)node.Attribute(attribute) ?? "", value =>
        {
            var error = value.Length == 0 ? null : validate(value);
            DataValidationErrors.SetError(box!, error is null ? null : new InvalidDataException(error));
            if (error is null) EditNode(id, $"Set {attribute}", n => CanvasEdit.SetAttribute(n, attribute, value));
        }, width);
        return box;
    }

    /// <summary>One binding: target with registry candidates, mode for properties, remove, and the vibe coding shortcut when broken.</summary>
    Control BindRow(XElement bind)
    {
        var isEvent = bind.Attribute("event") is not null;
        var name = (string?)bind.Attribute("event") ?? (string?)bind.Attribute("prop") ?? "";
        var target = new AutoCompleteBox { Text = (string?)bind.Attribute("target") ?? "", ItemsSource = Candidates(isEvent), FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinWidth = 220 };
        Commit(target, () => target.Text ?? "", value => EditBind(bind, "Set binding target", b => b.SetAttributeValue("target", value)));

        var inner = ((string?)bind.Attribute("nodeId") ?? "").Split('/', 2) is [_, var path] ? path + " · " : ""; // bound inside an instance
        var row = new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Children = { new TextBlock { Text = $"{inner}{name} {(isEvent ? "→" : "⇄")}", VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 }, target } };
        if (!isEvent)
        {
            var mode = new ComboBox { ItemsSource = new[] { "OneWay", "TwoWay" }, SelectedItem = (string?)bind.Attribute("mode") ?? "OneWay" };
            mode.SelectionChanged += (_, _) => EditBind(bind, "Set binding mode", b => b.SetAttributeValue("mode", (string)mode.SelectedItem!));
            row.Children.Add(mode);
        }
        var remove = new Button { Content = L.T("✕"), [ToolTip.TipProperty] = "Remove binding" };
        remove.Click += (_, _) => EditBind(bind, "Remove binding", b => b.Remove());
        row.Children.Add(remove);

        var panel = new StackPanel { Spacing = 4, Children = { row } };
        var nodeId = (string?)bind.Attribute("nodeId") ?? "";
        if (Workspace.Issues.FirstOrDefault(i => i.Screen == CanvasView.CurrentScreen && i.NodeId == nodeId && i.Target == (string?)bind.Attribute("target")) is { } issue)
        {
            panel.Children.Add(new TextBlock { Text = issue.Message, Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap });
            // Re-binding (spec §6): one click applies a near-name candidate.
            if (issue.Fix.Length > 0 && issue.Suggestions.Count > 0)
            {
                var candidates = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, Children = { new TextBlock { Text = L.T("Did you mean"), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 } } };
                foreach (var suggestion in issue.Suggestions)
                {
                    var apply = new Button { Content = new TextBlock { Text = suggestion, TextWrapping = TextWrapping.Wrap }, Padding = new(6, 2) };
                    apply.Click += (_, _) => EditBind(bind, "Rebind", b => b.SetAttributeValue(issue.Fix, suggestion));
                    candidates.Children.Add(apply);
                }
                panel.Children.Add(candidates);
            }
            if (CanvasView.ControlOf(nodeId) is { } control && CanvasView.VibeRequest(issue, control) is { } request)
            {
                var create = new Button { Content = L.T("Create with AI Chat") };
                create.Click += (_, _) => ChatView.Prefill(request);
                panel.Children.Add(create);
            }
        }
        return panel;
    }

    /// <summary>Adds a binding: event or property of the node's control (or, on an instance, of a node inside it), bound to a registry member.</summary>
    Control AddBindRow(string id, List<string> nodes)
    {
        var at = new ComboBox { ItemsSource = nodes, SelectedIndex = 0, IsVisible = nodes.Count > 1 };
        var kind = new ComboBox { ItemsSource = new[] { L.T("Event"), L.T("Property") }, SelectedIndex = 0 };
        var name = new AutoCompleteBox { MinWidth = 140, FilterMode = AutoCompleteFilterMode.ContainsOrdinal };
        void Names()
        {
            // Framework-neutral names only (Faro.Runtime.Bindable), so binding files don't depend on Avalonia.
            var bindable = CanvasView.ControlOf((string)at.SelectedItem!) is { } control ? Faro.Runtime.Bindable.For(control) : null;
            var names = (kind.SelectedIndex == 0 ? bindable?.Events.Keys.ToArray() : bindable?.Props.Keys.ToArray()) ?? [];
            name.ItemsSource = names;
            name.PlaceholderText = names.FirstOrDefault() ?? "";
        }
        Names();
        kind.SelectionChanged += (_, _) => Names();
        at.SelectionChanged += (_, _) => Names();
        var add = new Button { Content = L.T("+ Add binding") };
        add.Click += (_, _) =>
        {
            var member = string.IsNullOrWhiteSpace(name.Text) ? name.PlaceholderText ?? "" : name.Text.Trim();
            if (member.Length == 0) return;
            var isEvent = kind.SelectedIndex == 0;
            CanvasView.Edit("Add binding", (project, _) =>
            {
                var file = CanvasEdit.BindingsFileFor(project, CanvasView.CurrentScreen!);
                file.Root!.Add(new XElement("Bind", new XAttribute("nodeId", (string)at.SelectedItem!), new XAttribute(isEvent ? "event" : "prop", member), new XAttribute("target", ""),
                    isEvent ? null : new XAttribute("mode", "OneWay")));
                return [file];
            });
        };
        return new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Children = { at, kind, name, add } };
    }

    static void EditBind(XElement bind, string label, Action<XElement> change) => CanvasView.Edit(label, (_, _) =>
    {
        var doc = bind.Document!;
        change(bind);
        return [doc];
    });

    static string[] Candidates(bool isEvent) =>
        isEvent
            ? [.. Workspace.Registry.Where(m => m.IsMethod && m.Signature.EndsWith("()")).Select(m => m.Target),
               .. Workspace.Project?.Screens.Keys.Order().Select(s => $"Navigate:Screen.{s}") ?? []]
            : [.. Workspace.Registry.Where(m => !m.IsMethod).Select(m => m.Target)];

    static TextBox Field(string value, Action<string> commit, double width = 200)
    {
        var box = new TextBox { Text = value, MinWidth = width };
        Commit(box, () => box.Text ?? "", commit);
        return box;
    }

    /// <summary>Mock rows for the canvas (spec §10.5): one line per row, values in field order separated by "|".</summary>
    Control MockRows(string id, XElement node, List<string> fields)
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 60,
            Text = string.Join("\n", MockData.Rows(node).Select(r => string.Join(" | ", r))),
            PlaceholderText = string.Join(" | ", fields.Select(_ => "…")),
        };
        var last = box.Text;
        box.LostFocus += (_, _) =>
        {
            if (box.Text == last) return;
            last = box.Text;
            var rows = (box.Text ?? "").Split('\n').Where(l => l.Trim().Length > 0).Select(l => (IReadOnlyList<string>)[.. l.Split('|').Select(v => v.Trim())]).ToList();
            EditNode(id, "Set mock rows", n => MockData.SetRows(n, rows));
        };
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = L.F("Mock rows (canvas only): {0}", string.Join(" | ", fields)), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
                box,
            },
        };
    }

    /// <summary>Commits on Enter or when focus leaves, once per actual change (one history step per edit).</summary>
    static void Commit(Control box, Func<string> text, Action<string> commit)
    {
        var last = text();
        void Done()
        {
            if (text() == last) return;
            last = text();
            commit(last);
        }
        box.LostFocus += (_, _) => Done();
        box.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter) { Done(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    static Control Row(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new("90,*") };
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(caption);
        grid.Children.Add(editor);
        return grid;
    }

    /// <summary>Bindable paths inside an instance, nested instances included ("card/ok", "card/ok/label").</summary>
    static IEnumerable<string> InnerPaths(XElement instance, string prefix) =>
        CanvasEdit.InnerNodes(instance).Where(n => n != instance.Element("Node"))
            .SelectMany(n => InnerPaths(n, $"{prefix}/{(string?)n.Attribute("id")}").Prepend($"{prefix}/{(string?)n.Attribute("id")}"));

    /// <summary>An image path picked from Assets/ (typing a path still works).</summary>
    static Control AssetField(string value, Action<string> commit)
    {
        var box = new AutoCompleteBox { Text = value, ItemsSource = ProjectFiles.Images(Workspace.Root), FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinWidth = 200, PlaceholderText = L.T("Assets/…"), MinimumPrefixLength = 0 };
        Commit(box, () => box.Text ?? "", commit);
        return box;
    }

    static Control Pair(Control a, Control b) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { a, b } };

    static TextBlock Section(string title) => new() { Text = title, FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 0) };
    static TextBlock Hint(string text) => new() { Text = text, Opacity = 0.6, TextWrapping = TextWrapping.Wrap };
}
