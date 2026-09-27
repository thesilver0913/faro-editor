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
            body.Children.Add(Hint(CanvasView.Selection.Count > 1 ? $"{CanvasView.Selection.Count} nodes selected." : "Select a node on the canvas."));
            return;
        }
        var id = (string)node.Attribute("id")!;
        var type = (string?)node.Attribute("type") ?? "";

        body.Children.Add(new TextBlock { Text = type == "Instance" ? $"Instance of {(string?)node.Attribute("component")}" : type, FontWeight = FontWeight.SemiBold, FontSize = 15 });
        var idError = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(Row("ID", Field(id, value =>
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

        body.Children.Add(Section("Layout"));
        body.Children.Add(Row("Width", Sizing(node, "width")));
        body.Children.Add(Row("Height", Sizing(node, "height")));
        if (CanvasEdit.IsContainer(node))
        {
            if (type != "Container.Grid") body.Children.Add(Row("Direction", Choice(node, "direction", ["Vertical", "Horizontal"])));
            else body.Children.Add(Row("Columns", AttributeField(node, "columns")));
            body.Children.Add(Row("Gap", AttributeField(node, "gap")));
            body.Children.Add(Row("Padding", AttributeField(node, "padding")));
            body.Children.Add(Row("Alignment", Choice(node, "alignment", ["Start", "Center", "End"])));
        }

        var props = type == "Instance"
            ? node.Element("Node")?.Elements("Prop").Select(p => (string)p.Attribute("name")!).ToArray() ?? []
            : PropsByType.GetValueOrDefault(type, []);
        if (props.Length > 0)
        {
            body.Children.Add(Section(type == "Instance" ? "Overrides" : "Properties"));
            foreach (var prop in props)
                body.Children.Add(Row(prop, Field(CanvasEdit.GetProp(node, prop) ?? "", value => EditNode(id, $"Set {prop}", n => CanvasEdit.SetProp(n, prop, value)))));
        }
        if (type == "Instance")
        {
            var repeatable = new CheckBox { Content = "Repeatable (list)", IsChecked = (string?)node.Attribute("repeatable") == "true" };
            repeatable.IsCheckedChanged += (_, _) => EditNode(id, "Set repeatable", n => CanvasEdit.SetAttribute(n, "repeatable", repeatable.IsChecked == true ? "true" : null));
            body.Children.Add(repeatable);
        }

        body.Children.Add(Section("Bindings"));
        foreach (var bind in project.BindsFor(CanvasView.CurrentScreen!).Where(b => (string?)b.Attribute("nodeId") == id).ToList())
            body.Children.Add(BindRow(bind));
        body.Children.Add(AddBindRow(id));
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

    Control Choice(XElement node, string attribute, string[] values)
    {
        var id = (string)node.Attribute("id")!;
        var box = new ComboBox { ItemsSource = values, SelectedItem = (string?)node.Attribute(attribute) ?? values[0], MinWidth = 120 };
        box.SelectionChanged += (_, _) => EditNode(id, $"Set {attribute}", n => CanvasEdit.SetAttribute(n, attribute, (string)box.SelectedItem!));
        return box;
    }

    Control AttributeField(XElement node, string attribute)
    {
        var id = (string)node.Attribute("id")!;
        return Field((string?)node.Attribute(attribute) ?? "", value =>
        {
            if (value.Length > 0 && !double.TryParse(value, out _)) return; // numbers only; the canvas would fail to build otherwise
            EditNode(id, $"Set {attribute}", n => CanvasEdit.SetAttribute(n, attribute, value));
        }, 80);
    }

    /// <summary>One binding: target with registry candidates, mode for properties, remove, and the vibe coding shortcut when broken.</summary>
    Control BindRow(XElement bind)
    {
        var isEvent = bind.Attribute("event") is not null;
        var name = (string?)bind.Attribute("event") ?? (string?)bind.Attribute("prop") ?? "";
        var target = new AutoCompleteBox { Text = (string?)bind.Attribute("target") ?? "", ItemsSource = Candidates(isEvent), FilterMode = AutoCompleteFilterMode.ContainsOrdinal, MinWidth = 220 };
        Commit(target, () => target.Text ?? "", value => EditBind(bind, "Set binding target", b => b.SetAttributeValue("target", value)));

        var row = new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Children = { new TextBlock { Text = $"{name} {(isEvent ? "→" : "⇄")}", VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 }, target } };
        if (!isEvent)
        {
            var mode = new ComboBox { ItemsSource = new[] { "OneWay", "TwoWay" }, SelectedItem = (string?)bind.Attribute("mode") ?? "OneWay" };
            mode.SelectionChanged += (_, _) => EditBind(bind, "Set binding mode", b => b.SetAttributeValue("mode", (string)mode.SelectedItem!));
            row.Children.Add(mode);
        }
        var remove = new Button { Content = "✕", [ToolTip.TipProperty] = "Remove binding" };
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
                var candidates = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, Children = { new TextBlock { Text = "Did you mean", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 } } };
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
                var create = new Button { Content = "Create with vibe coding" };
                create.Click += (_, _) => ChatView.Prefill(request);
                panel.Children.Add(create);
            }
        }
        return panel;
    }

    /// <summary>Adds a binding: event or property of the node's control, bound to a registry member.</summary>
    Control AddBindRow(string id)
    {
        var kind = new ComboBox { ItemsSource = new[] { "Event", "Property" }, SelectedIndex = 0 };
        var name = new AutoCompleteBox { MinWidth = 140, FilterMode = AutoCompleteFilterMode.ContainsOrdinal };
        void Names()
        {
            // Framework-neutral names only (Faro.Runtime.Bindable), so binding files don't depend on Avalonia.
            var bindable = CanvasView.ControlOf(id) is { } control ? Faro.Runtime.Bindable.For(control) : null;
            var names = (kind.SelectedIndex == 0 ? bindable?.Events.Keys.ToArray() : bindable?.Props.Keys.ToArray()) ?? [];
            name.ItemsSource = names;
            name.PlaceholderText = names.FirstOrDefault() ?? "";
        }
        Names();
        kind.SelectionChanged += (_, _) => Names();
        var add = new Button { Content = "+ Add binding" };
        add.Click += (_, _) =>
        {
            var member = string.IsNullOrWhiteSpace(name.Text) ? name.PlaceholderText ?? "" : name.Text.Trim();
            if (member.Length == 0) return;
            var isEvent = kind.SelectedIndex == 0;
            CanvasView.Edit("Add binding", (project, _) =>
            {
                var file = CanvasEdit.BindingsFileFor(project, CanvasView.CurrentScreen!);
                file.Root!.Add(new XElement("Bind", new XAttribute("nodeId", id), new XAttribute(isEvent ? "event" : "prop", member), new XAttribute("target", ""),
                    isEvent ? null : new XAttribute("mode", "OneWay")));
                return [file];
            });
        };
        return new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Children = { kind, name, add } };
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

    static TextBlock Section(string title) => new() { Text = title, FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 0) };
    static TextBlock Hint(string text) => new() { Text = text, Opacity = 0.6, TextWrapping = TextWrapping.Wrap };
}
