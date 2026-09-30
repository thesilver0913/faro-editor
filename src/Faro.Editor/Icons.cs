using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Faro.Editor;

/// <summary>
/// Icons: node types are small hand-drawn geometries on a 24×24 grid (the Fluent symbol font has no layout icons);
/// toolbar actions use FluentAvalonia's symbols.
/// </summary>
public static class Icons
{
    static readonly Dictionary<string, string> NodePaths = new()
    {
        ["Container.Stack"] = "M4,4H20V8H4Z M4,10H20V14H4Z M4,16H20V20H4Z",
        ["Container.Wrap"] = "M3,5H10V10H3Z M12,5H21V10H12Z M3,14H14V19H3Z M16,14H21V19H16Z",
        ["Container.Grid"] = "M4,4H11V11H4Z M13,4H20V11H13Z M4,13H11V20H4Z M13,13H20V20H13Z",
        ["Container.Overlay"] = "M3,3H15V6H6V15H3Z M9,9H21V21H9Z",
        ["Control.Button"] = "M5,7H19A3,3 0 0 1 22,10V14A3,3 0 0 1 19,17H5A3,3 0 0 1 2,14V10A3,3 0 0 1 5,7Z M7,11V13H17V11Z",
        ["Control.TextInput"] = "M2,7H22V17H2Z M4,9V15H20V9Z M6,10H7.5V14H6Z",
        ["Control.Text"] = "M5,4H19V7.5H16.5V6.5H13.5V18H15.5V20H8.5V18H10.5V6.5H7.5V7.5H5Z",
        ["Control.Image"] = "M3,4H21V20H3Z M5,6V18H19V6Z M6,17L10,11L13,15L15,12.5L18,17Z M15.5,7.5A1.5,1.5 0 1 1 15.5,10.5A1.5,1.5 0 1 1 15.5,7.5Z",
        ["Control.CheckBox"] = "M4,4H20V20H4Z M6,6V18H18V6Z M8,12L10.5,14.5L16,9L17.4,10.4L10.5,17.3L6.6,13.4Z",
        ["Control.Switch"] = "M7,6H17A6,6 0 0 1 17,18H7A6,6 0 0 1 7,6Z M17,8A4,4 0 1 0 17,16A4,4 0 1 0 17,8Z",
        ["Control.Slider"] = "M2,11H22V13H2Z M9,7A5,5 0 1 1 9,17A5,5 0 1 1 9,7Z",
        ["Control.Select"] = "M2,6H22V18H2Z M4,8V16H20V8Z M13,11H19L16,14Z",
        ["Control.Progress"] = "M2,9H22V15H2Z M4,11V13H20V11Z M4,11H13V13H4Z",
        ["Control.Divider"] = "M2,11H22V13H2Z",
        ["Control.Spacer"] = "M2,6H4V18H2Z M20,6H22V18H20Z M6,11H18V13H6Z M6,12L9,9V15Z M18,12L15,9V15Z",
        ["Control.Script"] = "M9,4H7A2,2 0 0 0 5,6V10L3,12L5,14V18A2,2 0 0 0 7,20H9V18H7V13.5L5.5,12L7,10.5V6H9Z M15,4H17A2,2 0 0 1 19,6V10L21,12L19,14V18A2,2 0 0 1 17,20H15V18H17V13.5L18.5,12L17,10.5V6H15Z",
        ["Instance"] = "M12,2L15,5L12,8L9,5Z M5,9L8,12L5,15L2,12Z M19,9L22,12L19,15L16,12Z M12,16L15,19L12,22L9,19Z", // Figma-style component
    };

    /// <summary>Figma's align buttons: (horizontal, where) → the icon.</summary>
    static readonly Dictionary<(bool, string), string> AlignPaths = new()
    {
        [(true, "Start")] = "M3,3H5V21H3Z M7,6H17V10H7Z M7,14H21V18H7Z",
        [(true, "Center")] = "M11,3H13V21H11Z M6,6H18V10H6Z M4,14H20V18H4Z",
        [(true, "End")] = "M19,3H21V21H19Z M7,6H17V10H7Z M3,14H17V18H3Z",
        [(false, "Start")] = "M3,3H21V5H3Z M6,7H10V17H6Z M14,7H18V21H14Z",
        [(false, "Center")] = "M3,11H21V13H3Z M6,6H10V18H6Z M14,4H18V20H14Z",
        [(false, "End")] = "M3,19H21V21H3Z M6,7H10V17H6Z M14,3H18V17H14Z",
    };

    public static PathIcon Align(bool horizontal, string where) => new() { Data = Geometry.Parse(AlignPaths[(horizontal, where)]), Width = 16, Height = 16 };

    /// <summary>A tool of a tool panel: its icon (made fresh for each place it shows), tooltip and action.</summary>
    public sealed record Tool(Func<Control> Icon, string Tip, Action Run);

    /// <summary>A square tool button (Adobe's tool panel).</summary>
    public static Button ToolButton(Control icon, string tip, Action click)
    {
        var button = new Button { Content = icon, Width = 36, Height = 36, Padding = new(0), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, [ToolTip.TipProperty] = L.T(tip) };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>
    /// Grouped tools, as in Adobe's tool panel: the button shows the last tool used (a corner mark says there are more);
    /// clicking it opens the group beside it.
    /// </summary>
    public static Button ToolGroup(string tip, IReadOnlyList<Tool> tools)
    {
        var face = new Border { Child = tools[0].Icon() };
        var mark = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M6,0L6,6L0,6Z"), Fill = Brushes.Gray, Width = 6, Height = 6, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, Margin = new(0, 0, -8, -8) };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2 };
        var flyout = new Flyout { Content = row, Placement = PlacementMode.RightEdgeAlignedTop };
        foreach (var tool in tools)
            row.Children.Add(ToolButton(tool.Icon(), tool.Tip, () =>
            {
                flyout.Hide();
                face.Child = tool.Icon();
                tool.Run();
            }));
        Button? button = null;
        button = ToolButton(new Panel { Children = { face, mark } }, tip, () => flyout.ShowAt(button!));
        return button;
    }

    /// <summary>The icon of a node type (Instance: the component mark).</summary>
    public static PathIcon Node(string type, double size = 16) => new()
    {
        Data = Geometry.Parse(NodePaths.GetValueOrDefault(type, NodePaths["Control.Text"])),
        Width = size,
        Height = size,
    };

    /// <summary>A symbol followed by a label.</summary>
    public static StackPanel Label(FASymbol symbol, string text) => new()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal,
        Spacing = 6,
        Children = { new FASymbolIcon { Symbol = symbol, FontSize = 16 }, new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
    };

    /// <summary>A compact toolbar button: a symbol with its (translated) name as tooltip, or a symbol and a label.</summary>
    public static Button Button(FASymbol symbol, string tip, Action click, string? label = null)
    {
        var button = new Button
        {
            Content = label is null ? new FASymbolIcon { Symbol = symbol, FontSize = 16 } : Label(symbol, label),
            Padding = label is null ? new(6, 4) : new(8, 4),
            [ToolTip.TipProperty] = L.T(tip),
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Gives a toolbar's buttons, toggles and drop-downs (nested panels too) one height, so a row lines up.</summary>
    public static T Toolbar<T>(T bar) where T : Panel
    {
        foreach (var control in bar.Children)
            if (control is Panel inner) Toolbar(inner);
            else if (control is Avalonia.Controls.Button or ComboBox or Avalonia.Controls.Primitives.ToggleButton or TextBox)
            {
                control.Height = 32;
                control.MinWidth = 32;
                control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                if (control is ContentControl content) content.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
            }
        return bar;
    }

    /// <summary>Readable name of a node type: "Stack", "TextInput"…</summary>
    public static string Name(string type) => type.Split('.')[^1];
}
