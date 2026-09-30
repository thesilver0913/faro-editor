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
        ["Control.TextInput"] = ["Placeholder", "Text", "Password"],
        ["Control.NumberInput"] = ["Placeholder", "Value", "Minimum", "Maximum", "Step"],
        ["Control.DateInput"] = ["Placeholder", "Date"],
        ["Control.Text"] = ["Text"],
        ["Control.Image"] = ["Source"],
        ["Control.CheckBox"] = ["Text", "Checked"],
        ["Control.Switch"] = ["Text", "Checked"],
        ["Control.Slider"] = ["Value", "Minimum", "Maximum"],
        ["Control.Select"] = ["Options", "Selected"],
        ["Control.Progress"] = ["Value"],
        ["Control.Icon"] = ["Icon"],
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
                body.Children.Add(Row(prop, prop == "Icon" && type == "Control.Icon" ? IconField(CanvasEdit.GetProp(node, prop) ?? "star", value => EditNode(id, "Set Icon", n => CanvasEdit.SetProp(n, "Icon", value)))
                    : prop is "Source" or "BackgroundTexture"
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
        Appearance(node);
        // Design-language options (faro.json "design"): Material 3 Expressive per node, first value = the default (unset).
        var styled = type == "Instance" ? (string?)node.Element("Node")?.Attribute("type") ?? "" : type;
        if (Workspace.Project?.Design.Language == "Material3" && (M3Options.GetValueOrDefault(styled) ?? (styled.StartsWith("Container.") ? M3Options["Container"] : null)) is { } options)
        {
            body.Children.Add(Section(L.T("Material 3")));
            foreach (var (attribute, values) in options)
                // An instance shows (and falls back to) its master's or variant's value; picking another one overrides it.
                body.Children.Add(Row(L.T(char.ToUpperInvariant(attribute[3]) + attribute[4..]),
                    Choice(node, attribute, values, unset: type == "Instance" ? (string?)node.Element("Node")?.Attribute(attribute) ?? values[0] : values[0])));
        }
        // The table-driven languages (Cupertino … Retro) share their options: look.variant on buttons, look.surface on containers.
        if (Workspace.Project?.Design.Language is { } language && DesignLooks.Languages.Contains(language)
            && (LookOptions.GetValueOrDefault(styled) ?? (styled.StartsWith("Container.") ? LookOptions["Container"] : null)) is { } lookOptions)
        {
            body.Children.Add(Section(language));
            foreach (var (attribute, values) in lookOptions)
                body.Children.Add(Row(L.T(attribute == "look.surface" ? "Panel" : "Variant"),
                    Choice(node, attribute, values, unset: type == "Instance" ? (string?)node.Element("Node")?.Attribute(attribute) ?? values[0] : values[0])));
        }
        if (type == "Instance")
        {
            // Variants (Figma): the instance shows the master it picks; switching replaces its snapshot in the same step.
            body.Children.Add(Section(L.T("Component")));
            var component = (string?)node.Attribute("component") ?? "";
            var variants = ProjectFiles.Variants(project, component);
            var variant = new ComboBox { ItemsSource = variants.Prepend(L.T("(default)")).ToList(), SelectedIndex = variants.IndexOf((string?)node.Attribute("variant") ?? "") + 1, MinWidth = 140 };
            variant.SelectionChanged += (_, _) => EditNode(id, "Set variant", n =>
            {
                CanvasEdit.SetAttribute(n, "variant", variant.SelectedIndex > 0 ? variants[variant.SelectedIndex - 1] : null);
                if (project.Components.GetValueOrDefault(ComponentSync.MasterId(n))?.Root?.Element("Node") is { } master)
                {
                    n.Elements("Node").Remove();
                    n.Add(new XElement(master));
                }
            });
            var newVariant = new Button { Content = L.T("New variant…"), [ToolTip.TipProperty] = L.T("Copy the component as a variant (edit it like a master; bindings are shared)") };
            newVariant.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not Window owner || await Dialogs.Prompt(owner, L.T("New variant"), L.F("Variant name for {0}:", component), "Outlined") is not { Length: > 0 } name) return;
                try { UiHistory.CommitFiles("New variant", ProjectFiles.NewVariant(project, component, name.Trim())); }
                catch (ArgumentException ex) { await Dialogs.Info(owner, L.T("New variant"), new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap }); return; }
                Workspace.Reload();
                CanvasView.ShowScreen($"{component}@{name.Trim()}"); // edit the new variant on the canvas
            };
            body.Children.Add(Row(L.T("Component variant"), variant));
            body.Children.Add(newVariant);
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
        var list = (string?)node.Attribute("repeatable") == "true";
        // A list's own id is the list (Items); its rows' taps go on the row root, so that path is offered too.
        var rowRoot = list && node.Element("Node")?.Attribute("id") is { } root ? [$"{id}/{root.Value}"] : Array.Empty<string>();
        body.Children.Add(AddBindRow(id, [id, .. rowRoot, .. InnerPaths(node, id)], list));
        GroupSections();
    }

    static readonly HashSet<string> collapsedSections = [];

    /// <summary>Each section title and the rows under it become a collapsible group (remembered while Faro runs).</summary>
    void GroupSections()
    {
        var children = body.Children.ToList();
        body.Children.Clear();
        Panel target = body;
        foreach (var child in children)
        {
            if (child is TextBlock { Tag: "section", Text: { } title })
            {
                var content = new StackPanel { Spacing = body.Spacing };
                var expander = new Expander { Header = title, Content = content, IsExpanded = !collapsedSections.Contains(title), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                expander.PropertyChanged += (_, e) =>
                {
                    if (e.Property != Expander.IsExpandedProperty) return;
                    if (expander.IsExpanded) collapsedSections.Remove(title); else collapsedSections.Add(title);
                };
                body.Children.Add(expander);
                target = content;
                continue;
            }
            target.Children.Add(child);
        }
    }

    /// <summary>
    /// Colors and text (UiBuilder.Appearance): fill and text color, the text style token and its parts, and the colors while
    /// hovered, pressed and disabled. Colors take "#RRGGBB" or a color token; an instance's values override its master's.
    /// </summary>
    void Appearance(XElement node)
    {
        var tokens = Workspace.Project?.Tokens ?? new Dictionary<string, string>();
        string? TokenError(string v) => v.StartsWith('$') && !tokens.ContainsKey(v[1..]) ? L.F("No token {0} (File › Project Design… › Tokens).", v) : null;
        string? ColorError(string v) => TokenError(v) ?? (Color.TryParse(v.StartsWith('$') ? tokens[v[1..]] : v, out _) ? null : L.T("A color (#6750A4, #806750A4, Red) or a color token ($color.primary)."));
        Control ColorField(string attribute) => CheckedField(node, attribute, 84, ColorError);

        body.Children.Add(Section(L.T("Appearance")));
        body.Children.Add(Row(L.T("Fill / Text"), Pair(ColorField("background"), ColorField("foreground"))));
        // Text styles are token groups: text.title.fontSize, text.title.fontWeight… → "$text.title".
        var parts = new[] { ".fontFamily", ".fontSize", ".fontWeight", ".lineHeight" };
        var styles = tokens.Keys.Select(k => parts.FirstOrDefault(k.EndsWith) is { } p ? "$" + k[..^p.Length] : null).OfType<string>().Distinct().Order().Prepend(L.T("(none)")).ToList();
        var current = (string?)node.Attribute("textStyle");
        var style = new ComboBox { ItemsSource = styles, SelectedItem = current is null ? styles[0] : styles.Contains(current) ? current : null, MinWidth = 160, PlaceholderText = current };
        var id = (string)node.Attribute("id")!;
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedIndex < 0 || (string?)style.SelectedItem == (current ?? styles[0])) return;
            EditNode(id, "Set textStyle", n => CanvasEdit.SetAttribute(n, "textStyle", style.SelectedIndex == 0 ? null : (string)style.SelectedItem!));
        };
        body.Children.Add(Row(L.T("Text token"), style));
        var fonts = new Button { Content = "Aa", Padding = new(8, 2) };
        ToolTip.SetTip(fonts, L.T("Choose a font (project or online)"));
        fonts.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window owner && await FontPicker.Show(owner, Workspace.Root) is { } family)
                EditNode(id, "Set fontFamily", n => CanvasEdit.SetAttribute(n, "fontFamily", family));
        };
        body.Children.Add(Row(L.T("Font"), Pair(CheckedField(node, "fontFamily", 120, TokenError), fonts)));
        body.Children.Add(Row(L.T("Size / Line"), Pair(AttributeField(node, "fontSize"), AttributeField(node, "lineHeight"))));
        body.Children.Add(Row(L.T("Weight"), CheckedField(node, "fontWeight", 110, v => TokenError(v)
            ?? (UiBuilder.Weight(v.StartsWith('$') ? tokens[v[1..]] : v) is null ? L.T("Normal, Medium, SemiBold, Bold… or 100–900.") : null))));
        foreach (var state in new[] { "hover", "pressed", "disabled" })
            body.Children.Add(Row(L.T(char.ToUpperInvariant(state[0]) + state[1..]), Pair(ColorField(state + "Background"), ColorField(state + "Foreground"))));
        body.Children.Add(Hint(L.T("Fill / text colors per state: hover, pressed (buttons), disabled. Empty: the design language's own.")));
        body.Children.Add(Row(L.T("Animate (ms)"), AttributeField(node, "animate")));
        body.Children.Add(Hint(L.T("Color changes (states, bound colors) ease over this time, and the node fades in when shown.")));
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
        var mode = Segmented(["Fill", "Hug", "Fixed"], UiBuilder.Sizing(node, axis), value => EditNode(id, $"Set {axis}", n => CanvasEdit.SetAttribute(n, axis + "Sizing", value)));
        var size = AttributeField(node, axis);
        size.IsVisible = UiBuilder.Sizing(node, axis) == "Fixed";
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { mode, size } };
    }

    /// <summary>The Icon part's symbol: the bundled set (IconSet) and the project's (Assets/Icons), each with its picture; … adds one from online.</summary>
    Control IconField(string value, Action<string> set)
    {
        var box = new ComboBox
        {
            ItemsSource = IconLibrary.ProjectIcons(Workspace.Root).Concat(IconSet.Paths.Keys).Distinct().ToList(), SelectedItem = value, MinWidth = 160, MaxDropDownHeight = 360,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new FaroIcon { Icon = name, Width = 18, Height = 18, Root = Workspace.Root }, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center } },
            }),
        };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string name && name != value) set(name); };
        var more = new Button { Content = "…", Padding = new(8, 2), [ToolTip.TipProperty] = L.T("More icons (Material Symbols, online)") };
        more.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window owner && await IconLibrary.Pick(owner, Workspace.Root) is { } name) set(name);
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { box, more } };
    }

    static readonly Dictionary<string, (string Attribute, string[] Values)[]> LookOptions = new()
    {
        ["Control.Button"] = [("look.variant", ["Filled", "Tonal", "Outlined", "Text"])],
        ["Container"] = [("look.surface", ["None", "Card", "Inset"])],
    };

    static readonly Dictionary<string, (string Attribute, string[] Values)[]> M3Options = new()
    {
        ["Control.Button"] = [("m3.variant", ["Filled", "Tonal", "Outlined", "Text", "Elevated"]), ("m3.size", ["S", "XS", "M", "L", "XL"]), ("m3.shape", ["Round", "Square"])],
        ["Control.TextInput"] = [("m3.variant", ["Filled", "Outlined"])],
        ["Control.Text"] = [("m3.type", ["BodyLarge", "DisplayLarge", "DisplayMedium", "DisplaySmall", "HeadlineLarge", "HeadlineMedium", "HeadlineSmall",
                "TitleLarge", "TitleMedium", "TitleSmall", "BodyMedium", "BodySmall", "LabelLarge", "LabelMedium", "LabelSmall"]),
            ("m3.emphasized", ["false", "true"]), ("m3.color", ["OnSurface", "OnSurfaceVariant", "Primary", "Secondary", "Tertiary", "Error"])],
        ["Container"] = [("m3.surface", ["None", "Surface", "Lowest", "Low", "Container", "High", "Highest", "Primary", "Secondary", "Tertiary"]),
            ("m3.corner", ["None", "XS", "S", "M", "L", "XL", "Full"]), ("m3.elevation", ["0", "1", "2", "3", "4", "5"])],
    };

    /// <param name="unset">A choice that removes the attribute (e.g. "Auto": follow the container).</param>
    Control Choice(XElement node, string attribute, string[] values, string? unset = null)
    {
        var id = (string)node.Attribute("id")!;
        void Set(string value) => EditNode(id, $"Set {attribute}", n => CanvasEdit.SetAttribute(n, attribute, value == unset ? null : value));
        var current = (string?)node.Attribute(attribute) ?? unset ?? values[0];
        if (values.Length <= 4) return Segmented(values, current, Set);
        var box = new ComboBox { ItemsSource = values, SelectedItem = current, MinWidth = 120 };
        box.SelectionChanged += (_, _) => Set((string)box.SelectedItem!);
        return box;
    }

    /// <summary>A few choices side by side (Figma's alignment buttons) instead of a drop-down.</summary>
    static Control Segmented(string[] values, string current, Action<string> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var value in values)
        {
            var button = new Avalonia.Controls.Primitives.ToggleButton { Content = L.T(value, "layout"), IsChecked = value == current, Padding = new(6, 3), FontSize = 12, MinWidth = 0 };
            button.Click += (_, _) => { if (value != current) set(value); else button.IsChecked = true; };
            row.Children.Add(button);
        }
        return row;
    }

    /// <param name="sides">Padding/margin: 1, 2 or 4 numbers ("8", "8 16", "8 16 8 16": top right bottom left).</param>
    Control AttributeField(XElement node, string attribute, bool sides = false, bool integer = false) =>
        CheckedField(node, attribute, 80, value =>
        {
            var numbers = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (numbers.FirstOrDefault(v => v.StartsWith('$') && Workspace.Project?.Tokens.ContainsKey(v[1..]) != true) is { } unknown)
                return L.F("No token {0} (File › Project Design… › Tokens).", unknown);
            return numbers.Where(v => !v.StartsWith('$')).Any(v => integer ? !int.TryParse(v, out var i) || i < 0 : !double.TryParse(v, out _)) ? L.T(integer ? "A whole number (0 or more)." : "Numbers only.") // "$space.m": a token
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
        if (isEvent && ((string?)bind.Attribute("target"))?.StartsWith("Navigate:") == true)
        {
            // How the screen changes (FaroApp.Navigate's transition); unset: faro.json's "transition" (Slide).
            string[] kinds = [L.F("Default ({0})", Workspace.Project?.Transition ?? "Slide"), "Slide", "Fade", "None"];
            var transition = new ComboBox { ItemsSource = kinds, SelectedIndex = Math.Max(0, Array.IndexOf(kinds, (string?)bind.Attribute("transition"))), [ToolTip.TipProperty] = L.T("Screen transition") };
            transition.SelectionChanged += (_, _) => EditBind(bind, "Set screen transition", b => b.SetAttributeValue("transition", transition.SelectedIndex > 0 ? kinds[transition.SelectedIndex] : null));
            row.Children.Add(transition);
        }
        if (!isEvent)
        {
            var mode = new ComboBox { ItemsSource = new[] { "OneWay", "TwoWay" }, SelectedItem = (string?)bind.Attribute("mode") ?? "OneWay" };
            mode.SelectionChanged += (_, _) => EditBind(bind, "Set binding mode", b => b.SetAttributeValue("mode", (string)mode.SelectedItem!));
            row.Children.Add(mode);
            // Display format ("¥{0:N0}"): {0}, N<decimals> (thousands separators) and F<decimals> work the same in C# and Java.
            var format = new TextBox { Text = (string?)bind.Attribute("format") ?? "", PlaceholderText = L.T("Format, e.g. ¥{0:N0}"), MinWidth = 120, [ToolTip.TipProperty] = L.T("{0} is the value; {0:N0} adds thousands separators, {0:F2} two decimals") };
            Commit(format, () => format.Text ?? "", value => EditBind(bind, "Set binding format", b => b.SetAttributeValue("format", value.Length > 0 ? value : null)));
            row.Children.Add(format);
        }
        var remove = new Button { Content = L.T("✕"), [ToolTip.TipProperty] = L.T("Remove binding") };
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
    Control AddBindRow(string id, List<string> nodes, bool list = false)
    {
        var at = new ComboBox { ItemsSource = nodes, SelectedIndex = 0, IsVisible = nodes.Count > 1 };
        var kind = new ComboBox { ItemsSource = new[] { L.T("Event"), L.T("Property") }, SelectedIndex = 0 };
        var name = new AutoCompleteBox { MinWidth = 140, FilterMode = AutoCompleteFilterMode.ContainsOrdinal };
        void Names()
        {
            // Framework-neutral names only (Faro.Runtime.Bindable), so binding files don't depend on Avalonia.
            var bindable = CanvasView.ControlOf((string)at.SelectedItem!) is { } control ? Faro.Runtime.Bindable.For(control) : null;
            var names = (kind.SelectedIndex == 0 ? bindable?.Events.Keys.ToArray() : bindable?.Props.Keys.ToArray()) ?? [];
            if (list && (string)at.SelectedItem! == id) names = kind.SelectedIndex == 1 ? ["Items", .. names] : []; // a list's rows: one per item; taps go on a row
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
            ? [.. Workspace.Registry.Where(m => m.IsMethod && !m.Signature.Contains(',')).Select(m => m.Target), // no parameter, or one: the row's item / the screen's parameter
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

    static TextBlock Section(string title) => new() { Text = title, Tag = "section" };
    static TextBlock Hint(string text) => new() { Text = text, Opacity = 0.6, TextWrapping = TextWrapping.Wrap };
}
