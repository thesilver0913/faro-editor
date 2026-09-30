using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>
/// Parts to add (Figma's assets / Android Studio's palette): node types and the project's components as tiles.
/// Click adds into the selected container (or after the selected node); drag drops it where the pointer is on the canvas.
/// </summary>
public sealed class PaletteView : UserControl
{
    /// <summary>Dragged part: a node type ("Control.Button") or "Instance:" + a component id.</summary>
    public static readonly DataFormat<string> PartFormat = DataFormat.CreateInProcessFormat<string>("faro-part");

    static readonly Dictionary<string, string> Tips = new()
    {
        ["Container.Stack"] = "Children in a column or a row (auto layout)",
        ["Container.Wrap"] = "Children in rows that wrap",
        ["Container.Grid"] = "Children in rows and columns",
        ["Container.Overlay"] = "Children stacked on top of each other, anchored to edges",
        ["Control.Button"] = "A button (Click event)",
        ["Control.TextInput"] = "A text field (Text, Changed)",
        ["Control.NumberInput"] = "A number field with up/down (Value, Changed)",
        ["Control.DateInput"] = "A date field with a calendar (Date as yyyy-MM-dd)",
        ["Control.Text"] = "A label",
        ["Control.Image"] = "An image from Assets/",
        ["Control.CheckBox"] = "A check box (Checked, Changed)",
        ["Control.Switch"] = "An on/off switch (Checked, Changed)",
        ["Control.Slider"] = "A slider (Value, Changed)",
        ["Control.Select"] = "A drop-down of choices (Options, Selected, Changed)",
        ["Control.Progress"] = "A progress bar, 0 to 100 (Value)",
        ["Control.Icon"] = "A symbol from the bundled icon set (Icon), in the text color",
        ["Control.Divider"] = "A thin line between parts",
        ["Control.Spacer"] = "Empty space that pushes its neighbors apart (e.g. a button to the right end)",
        ["Control.Script"] = "A part built in code (FaroScript)",
    };

    readonly StackPanel body = new() { Spacing = 4, Margin = new(8) };

    public PaletteView() => Content = new ScrollViewer { Content = body };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Build;
        Build();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Build;
        base.OnDetachedFromVisualTree(e);
    }

    void Build()
    {
        body.Children.Clear();
        Section("Containers", CanvasEdit.AddableTypes.Where(t => t.StartsWith("Container.")).Select(t => (t, Icons.Name(t), L.T(Tips[t]))));
        Section("Controls", CanvasEdit.AddableTypes.Where(t => t.StartsWith("Control.")).Select(t => (t, Icons.Name(t), L.T(Tips[t]))));
        Section("Components", (Workspace.Project?.Components.Keys.Order() ?? Enumerable.Empty<string>()).Select(c => ("Instance:" + c, c.StartsWith("Comp.") ? c[5..] : c, c)));
    }

    void Section(string title, IEnumerable<(string Part, string Label, string Tip)> parts)
    {
        var tiles = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        foreach (var (part, label, tip) in parts) tiles.Children.Add(Tile(part, label, tip));
        if (tiles.Children.Count == 0) return;
        body.Children.Add(new TextBlock { Text = L.T(title), Opacity = 0.7, FontSize = 12, Margin = new(0, 6, 0, 0) });
        body.Children.Add(tiles);
    }

    static Control Tile(string part, string label, string tip)
    {
        var isInstance = part.StartsWith("Instance:");
        var tile = new Button
        {
            Width = 76,
            Height = 62,
            Padding = new(4),
            [ToolTip.TipProperty] = tip,
            Content = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new ContentControl { Content = Icons.Node(isInstance ? "Instance" : part, 22), HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = label, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        tile.Click += (_, _) => Add(part);
        PointerPressedEventArgs? pressed = null;
        tile.AddHandler(PointerPressedEvent, (_, e) => pressed = e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed ? e : null, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        tile.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pressed is not { } start || Point.Distance(start.GetPosition(tile), e.GetPosition(tile)) < 6) return;
            pressed = null;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(PartFormat, part));
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return tile;
    }

    /// <summary>Adds a part next to the selection (the same rule as the canvas's Add menu).</summary>
    public static void Add(string part)
    {
        if (part.StartsWith("Instance:")) CanvasView.AddNode("Instance", part["Instance:".Length..]);
        else CanvasView.AddNode(part);
    }
}
