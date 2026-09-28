using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Faro.Runtime;

/// <summary>
/// Turns a UIGraph &lt;Node&gt; tree (spec §5) into Avalonia controls. Used by both the running app
/// and the editor canvas. Every built node is registered in <c>byId</c>; nodes inside an instance
/// are registered as "instanceId/innerId".
/// </summary>
public static class UiBuilder
{
    public static Control Build(XElement node, IDictionary<string, Control> byId, string projectRoot, string prefix = "")
    {
        var type = (string?)node.Attribute("type") ?? "";
        Control control = type switch
        {
            "Container.Stack" or "Container.Wrap" or "Container.Grid" or "Container.Overlay" => Container(node, type, byId, projectRoot, prefix),
            "Instance" => Instance(node, byId, projectRoot, prefix),
            "Control.Script" => Script((string?)node.Attribute("class") ?? ""),
            "Control.Button" => new Button { Content = Prop(node, "Text") },
            "Control.TextInput" => new TextBox { PlaceholderText = Prop(node, "Placeholder"), Text = Prop(node, "Text") },
            "Control.Text" => new TextBlock { Text = Prop(node, "Text"), TextWrapping = TextWrapping.Wrap },
            "Control.Image" => new Image { Source = Bitmap(projectRoot, Prop(node, "Source")) },
            _ => new TextBlock { Text = $"[unknown type: {type}]", Foreground = Brushes.Red },
        };
        if (Prop(node, "BackgroundTexture") is { } texture && control is TemplatedControl templated)
            templated.Background = new ImageBrush(Bitmap(projectRoot, texture)) { Stretch = Stretch.UniformToFill };
        if (Sizing(node, "width") == "Fixed" && (double?)node.Attribute("width") is { } w) control.Width = w;
        if (Sizing(node, "height") == "Fixed" && (double?)node.Attribute("height") is { } h) control.Height = h;
        if ((double?)node.Attribute("minWidth") is { } minW) control.MinWidth = minW;
        if ((double?)node.Attribute("maxWidth") is { } maxW) control.MaxWidth = maxW;
        if ((double?)node.Attribute("minHeight") is { } minH) control.MinHeight = minH;
        if ((double?)node.Attribute("maxHeight") is { } maxH) control.MaxHeight = maxH;
        if (node.Attribute("margin") is { } margin) control.Margin = Sides(margin.Value);
        byId[prefix + (string?)node.Attribute("id")] = control;
        return control;
    }

    /// <summary>
    /// Builds a Script node's control from its class name: the app resolves it in the user assembly, the editor
    /// canvas in the last build. Unset (or failing): a placeholder naming the class.
    /// </summary>
    public static Func<string, Control>? ScriptFactory { get; set; }

    /// <summary>The script's control inside a ContentControl, so bindings see a Script node (Visible, Enabled) whatever it builds.</summary>
    static Control Script(string name)
    {
        Control content;
        try { content = ScriptFactory?.Invoke(name) ?? Placeholder($"Script: {name}", Brushes.Gray); }
        catch (Exception e) { content = Placeholder($"Script: {name}\n{(e as System.Reflection.TargetInvocationException)?.InnerException?.Message ?? e.Message}", Brushes.OrangeRed); }
        return new ContentControl { Content = content };
    }

    static Control Placeholder(string text, IBrush color) => new Border
    {
        BorderBrush = color,
        BorderThickness = new(1),
        Padding = new(8),
        MinHeight = 40,
        Child = new TextBlock { Text = text, Foreground = color, TextWrapping = TextWrapping.Wrap },
    };

    /// <summary>Fill / Hug / Fixed per axis: widthSizing/heightSizing, with "sizing" as shorthand for both. Default Hug.</summary>
    public static string Sizing(XElement node, string axis) =>
        (string?)node.Attribute(axis + "Sizing") ?? (string?)node.Attribute("sizing") ?? "Hug";

    /// <summary>
    /// Every instance under a node with the key prefix its inner nodes get in <c>byId</c> ("btn1/", nested
    /// "card/btn1/") and its component — so component-level bindings can reach each instance's controls.
    /// </summary>
    public static IEnumerable<(string Prefix, string Component)> InstancePaths(XElement node, string prefix = "")
    {
        if ((string?)node.Attribute("type") == "Instance")
        {
            var inner = $"{prefix}{(string?)node.Attribute("id")}/";
            yield return (inner, (string?)node.Attribute("component") ?? "");
            if (node.Element("Node") is { } snapshot)
                foreach (var nested in InstancePaths(snapshot, inner)) yield return nested;
            yield break;
        }
        foreach (var child in node.Elements("Node"))
            foreach (var path in InstancePaths(child, prefix)) yield return path;
    }

    public static string? Prop(XElement node, string name) =>
        (string?)node.Elements("Prop").FirstOrDefault(p => (string?)p.Attribute("name") == name)?.Attribute("value");

    /// <summary>
    /// Containers follow Figma Auto Layout: Stack is a Grid with one Auto/Star track per child, so Fill children
    /// share the main axis (by "weight") and Hug/Fixed children take their own size; "justify" packs the rest at
    /// Start/Center/End or spreads it (SpaceBetween). On the cross axis Fill stretches, others follow "alignment"
    /// (or their own "alignSelf"). Overlay layers its children, each anchored to the container's edges or center
    /// (Figma constraints / Android Box): "anchorX" Left/Center/Right, "anchorY" Top/Center/Bottom, Fill stretches,
    /// "margin" keeps the distance. No absolute coordinates anywhere.
    /// </summary>
    static Control Container(XElement node, string type, IDictionary<string, Control> byId, string root, string prefix)
    {
        var vertical = (string?)node.Attribute("direction") != "Horizontal";
        var gap = (double?)node.Attribute("gap") ?? 0;
        var align = (string?)node.Attribute("alignment");
        var children = node.Elements("Node").Select(n => (Node: n, Control: Build(n, byId, root, prefix))).ToList();

        Panel panel;
        if (type == "Container.Stack")
        {
            var justify = (string?)node.Attribute("justify") ?? "Start";
            var anyFill = children.Any(c => Sizing(c.Node, vertical ? "height" : "width") == "Fill");
            var spread = justify == "SpaceBetween" && !anyFill && children.Count > 1;
            var grid = new Grid { RowSpacing = spread ? 0 : gap, ColumnSpacing = spread ? 0 : gap };
            var track = 0;
            void Add(GridLength length, Control? c)
            {
                if (vertical) { grid.RowDefinitions.Add(new RowDefinition(length)); if (c is not null) Grid.SetRow(c, track); }
                else { grid.ColumnDefinitions.Add(new ColumnDefinition(length)); if (c is not null) Grid.SetColumn(c, track); }
                track++;
            }
            for (var i = 0; i < children.Count; i++)
            {
                var (n, c) = children[i];
                if (spread && i > 0) Add(GridLength.Star, null); // the leftover space goes between the children
                Add(Sizing(n, vertical ? "height" : "width") == "Fill" ? new GridLength((double?)n.Attribute("weight") ?? 1, GridUnitType.Star) : GridLength.Auto, c);
            }
            // Packed at Start/Center/End: the grid hugs its children inside the container (Fill children need the whole length).
            if (!anyFill && !spread && justify is "Center" or "End")
            {
                if (vertical) grid.VerticalAlignment = justify == "Center" ? VerticalAlignment.Center : VerticalAlignment.Bottom;
                else grid.HorizontalAlignment = justify == "Center" ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            }
            panel = grid;
        }
        else if (type == "Container.Overlay")
            panel = new Grid(); // one cell: children stack up in z-order, placed by their anchors below
        else if (type == "Container.Wrap")
            panel = new WrapPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, ItemSpacing = gap, LineSpacing = gap };
        else // ponytail: Grid = uniform grid with a "columns" attribute; explicit row/column tracks when a design needs them
            panel = new UniformGrid { Columns = (int?)node.Attribute("columns") ?? 2, RowSpacing = gap, ColumnSpacing = gap };

        foreach (var (n, c) in children)
        {
            if (type == "Container.Overlay")
            {
                c.HorizontalAlignment = Sizing(n, "width") == "Fill" ? HorizontalAlignment.Stretch
                    : (string?)n.Attribute("anchorX") switch { "Center" => HorizontalAlignment.Center, "Right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left };
                c.VerticalAlignment = Sizing(n, "height") == "Fill" ? VerticalAlignment.Stretch
                    : (string?)n.Attribute("anchorY") switch { "Center" => VerticalAlignment.Center, "Bottom" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top };
            }
            else
            {
                var fill = Sizing(n, vertical ? "width" : "height") == "Fill";
                var self = (string?)n.Attribute("alignSelf") ?? align;
                if (vertical) c.HorizontalAlignment = fill ? HorizontalAlignment.Stretch : self switch { "Center" => HorizontalAlignment.Center, "End" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left };
                else c.VerticalAlignment = fill ? VerticalAlignment.Stretch : self switch { "Center" => VerticalAlignment.Center, "End" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top };
            }
            panel.Children.Add(c);
        }
        return new Border { Padding = Sides((string?)node.Attribute("padding")), Child = panel };
    }

    /// <summary>
    /// "16", "8 16" (vertical horizontal) or "8 16 4 16" (top right bottom left), CSS order; commas work too.
    /// Converted here because Avalonia's Thickness order differs (left top right bottom).
    /// </summary>
    public static Thickness Sides(string? text)
    {
        var v = (text ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(x => double.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0).ToArray();
        return v switch
        {
            [var all] => new(all),
            [var y, var x] => new(x, y, x, y),
            [var top, var right, var bottom, var left] => new(left, top, right, bottom),
            _ => new(0),
        };
    }

    /// <summary>
    /// Instances render the master snapshot stored inside them (updated only by the explicit
    /// component sync, see ComponentSync) with their &lt;Override&gt;s applied.
    /// </summary>
    static Control Instance(XElement node, IDictionary<string, Control> byId, string root, string prefix)
    {
        if (node.Element("Node") is not { } snapshot)
            return new TextBlock { Text = $"[not synced: {(string?)node.Attribute("component")}]", Foreground = Brushes.OrangeRed };
        var copy = new XElement(snapshot);
        foreach (var o in node.Elements("Override"))
        {
            copy.Elements("Prop").Where(p => (string?)p.Attribute("name") == (string?)o.Attribute("prop")).Remove();
            copy.Add(new XElement("Prop", new XAttribute("name", (string?)o.Attribute("prop") ?? ""), new XAttribute("value", (string?)o.Attribute("value") ?? "")));
        }
        return Build(copy, byId, root, $"{prefix}{(string?)node.Attribute("id")}/");
    }

    static Bitmap? Bitmap(string root, string? path)
    {
        var full = path is null ? null : Path.Combine(root, path);
        return File.Exists(full) ? new Bitmap(full) : null;
    }
}
