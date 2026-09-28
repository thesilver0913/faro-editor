using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Faro.Runtime;

/// <summary>
/// The framework-neutral event/property names a &lt;Bind&gt; may use per node type (spec §6), mapped to their
/// Avalonia counterparts. Binding files only ever hold the neutral names; this table is the Avalonia adapter.
/// </summary>
public static class Bindable
{
    public sealed record Entry(string Type, Type Control, Dictionary<string, RoutedEvent> Events, Dictionary<string, AvaloniaProperty> Props);

    static Dictionary<string, AvaloniaProperty> Common(params (string, AvaloniaProperty)[] props) =>
        props.Append(("Visible", Visual.IsVisibleProperty)).Append(("Enabled", InputElement.IsEnabledProperty)).ToDictionary();

    // Order matters for For(control): Button before the generic Control base types.
    static readonly Entry[] entries =
    [
        new("Control.Button", typeof(Button), new() { ["Click"] = Button.ClickEvent }, Common(("Text", ContentControl.ContentProperty))),
        new("Control.Script", typeof(ContentControl), [], Common()), // after Button, which is a ContentControl too
        new("Control.TextInput", typeof(TextBox), new() { ["Changed"] = TextBox.TextChangedEvent },
            Common(("Text", TextBox.TextProperty), ("Placeholder", TextBox.PlaceholderTextProperty))),
        new("Control.Text", typeof(TextBlock), [], Common(("Text", TextBlock.TextProperty))),
        new("Control.Image", typeof(Image), [], Common()),
        new("Container.", typeof(Border), new() { ["Click"] = InputElement.TappedEvent }, Common()), // Stack / Wrap / Grid; Click: a tap (e.g. a list row)
    ];

    /// <summary>By node type ("Container.Stack" matches the "Container." entry).</summary>
    public static Entry? For(string nodeType) => entries.FirstOrDefault(e => nodeType == e.Type || e.Type.EndsWith('.') && nodeType.StartsWith(e.Type));

    /// <summary>By built control (the runtime only has the control at bind time).</summary>
    public static Entry? For(Control control) => entries.FirstOrDefault(e => e.Control.IsInstanceOfType(control));

    /// <summary>The type a bind on this node sees: an instance is its master's root (from the synced snapshot).</summary>
    public static string TypeOf(XElement node) =>
        (string?)node.Attribute("type") is "Instance" && node.Element("Node") is { } snapshot ? TypeOf(snapshot) : (string?)node.Attribute("type") ?? "";

    /// <summary>The value type user code should expose for a property (Button's Content is object, but Text is text).</summary>
    public static Type ValueType(AvaloniaProperty property) => property == ContentControl.ContentProperty ? typeof(string) : property.PropertyType;
}
