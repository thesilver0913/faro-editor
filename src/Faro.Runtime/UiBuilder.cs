using System.Xml.Linq;
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
            "Container.Stack" or "Container.Wrap" or "Container.Grid" => Container(node, type, byId, projectRoot, prefix),
            "Instance" => Instance(node, byId, projectRoot, prefix),
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
        byId[prefix + (string?)node.Attribute("id")] = control;
        return control;
    }

    /// <summary>Fill / Hug / Fixed per axis: widthSizing/heightSizing, with "sizing" as shorthand for both. Default Hug.</summary>
    public static string Sizing(XElement node, string axis) =>
        (string?)node.Attribute(axis + "Sizing") ?? (string?)node.Attribute("sizing") ?? "Hug";

    public static string? Prop(XElement node, string name) =>
        (string?)node.Elements("Prop").FirstOrDefault(p => (string?)p.Attribute("name") == name)?.Attribute("value");

    /// <summary>
    /// Containers follow Figma Auto Layout: Stack is a Grid with one Auto/Star track per child,
    /// so Fill children share the main axis and Hug/Fixed children take their own size;
    /// on the cross axis Fill stretches and Hug/Fixed follow the container's alignment.
    /// </summary>
    static Control Container(XElement node, string type, IDictionary<string, Control> byId, string root, string prefix)
    {
        var vertical = (string?)node.Attribute("direction") != "Horizontal";
        var gap = (double?)node.Attribute("gap") ?? 0;
        var align = (string?)node.Attribute("alignment");
        var hAlign = align switch { "Center" => HorizontalAlignment.Center, "End" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left };
        var vAlign = align switch { "Center" => VerticalAlignment.Center, "End" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top };
        var children = node.Elements("Node").Select(n => (Node: n, Control: Build(n, byId, root, prefix))).ToList();

        Panel panel;
        if (type == "Container.Stack")
        {
            var grid = new Grid { RowSpacing = gap, ColumnSpacing = gap };
            for (var i = 0; i < children.Count; i++)
            {
                var (n, c) = children[i];
                var length = Sizing(n, vertical ? "height" : "width") == "Fill" ? GridLength.Star : GridLength.Auto;
                if (vertical) { grid.RowDefinitions.Add(new RowDefinition(length)); Grid.SetRow(c, i); }
                else { grid.ColumnDefinitions.Add(new ColumnDefinition(length)); Grid.SetColumn(c, i); }
            }
            panel = grid;
        }
        else if (type == "Container.Wrap")
            panel = new WrapPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, ItemSpacing = gap, LineSpacing = gap };
        else // ponytail: Grid = uniform grid with a "columns" attribute; explicit row/column tracks when a design needs them
            panel = new UniformGrid { Columns = (int?)node.Attribute("columns") ?? 2, RowSpacing = gap, ColumnSpacing = gap };

        foreach (var (n, c) in children)
        {
            var fill = Sizing(n, vertical ? "width" : "height") == "Fill";
            if (vertical) c.HorizontalAlignment = fill ? HorizontalAlignment.Stretch : hAlign;
            else c.VerticalAlignment = fill ? VerticalAlignment.Stretch : vAlign;
            panel.Children.Add(c);
        }
        return new Border { Padding = new((double?)node.Attribute("padding") ?? 0), Child = panel };
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
