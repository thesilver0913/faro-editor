using System.Xml.Linq;
using Avalonia.Media;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// Accessibility warnings for Problems (like Android Studio's lint): text contrast below WCAG AA (the design language's
/// own colors and each node's colors, tokens resolved), touch targets smaller than 44px, and text fields without a hint
/// or a label. Warnings only: nothing breaks, and the canvas badges stay for broken bindings.
/// </summary>
public static class AccessibilityCheck
{
    const double MinTarget = 44; // Apple's 44pt; Material asks for 48dp
    static readonly string[] Tappable = ["Control.Button", "Control.CheckBox", "Control.Switch", "Control.Slider", "Control.Select", "Control.TextInput"];

    public static List<BindingIssue> Check(FaroProject project)
    {
        var issues = new List<BindingIssue>();
        string Token(string value) => value.StartsWith('$') && project.Tokens.TryGetValue(value[1..], out var token) ? token : value;
        Color? ColorOf(XElement node, string name) => node.Attribute(name) is { } a && Color.TryParse(Token(a.Value.Trim()), out var c) ? c : null;
        var themes = project.Design.Theme switch { "Light" => new[] { false }, "Dark" => [true], _ => [false, true] };

        // The language's own pairs: button label on its fill, text and hints on the surface and in fields
        foreach (var dark in themes)
            if (Colors(project.Design, dark) is { } c)
                foreach (var (what, fore, back, min) in new[]
                {
                    ("button text", c.OnButton, c.Button, 4.5), ("text", c.OnSurface, c.Surface, 4.5), ("hint text in fields", c.Muted, c.Field, 3.0),
                })
                    if (Ratio(fore, back) is var ratio && ratio < min)
                        issues.Add(new("", "", L.F("{0} ({1}): the {2} has a contrast of {3:0.0}:1 (at least {4}:1 is recommended).",
                            project.Design.Language, L.T(dark ? "Dark" : "Light"), L.T(what), ratio, min), [], ""));

        var surface = Colors(project.Design, false)?.Surface ?? Avalonia.Media.Colors.White;
        foreach (var (graphId, graph) in project.Screens.Concat(project.Components))
            foreach (var node in BindingCheck.NodesOf(graph))
            {
                var id = (string?)node.Attribute("id") ?? "";
                var type = (string?)node.Attribute("type") ?? "";
                // Colors set on the node: its text against its own fill, or the nearest fill behind it
                if (ColorOf(node, "foreground") is { } fore)
                {
                    var back = node.AncestorsAndSelf("Node").Select(n => ColorOf(n, "background")).FirstOrDefault(b => b is not null) ?? surface;
                    if (Ratio(fore, back) is var ratio && ratio < 4.5)
                        issues.Add(new(id, "", L.F("Text contrast is {0:0.0}:1 (at least 4.5:1 is recommended).", ratio), [], graphId));
                }
                // A fixed size under 44px is hard to tap
                if (Tappable.Contains(type))
                    foreach (var axis in new[] { "width", "height" })
                        if (UiBuilder.Sizing(node, axis) == "Fixed" && double.TryParse(Token((string?)node.Attribute(axis) ?? ""), System.Globalization.CultureInfo.InvariantCulture, out var size) && size < MinTarget)
                            issues.Add(new(id, "", L.F("The {0} is {1}px: at least 44px is easier to tap.", L.T(axis), size), [], graphId));
                // A text field needs a hint or a label just before it
                if (type == "Control.TextInput" && !node.Elements("Prop").Any(p => (string?)p.Attribute("name") == "Placeholder" && ((string?)p.Attribute("value"))?.Length > 0)
                    && (string?)(node.ElementsBeforeSelf("Node").LastOrDefault()?.Attribute("type")) != "Control.Text")
                    issues.Add(new(id, "", L.T("This text field has no placeholder and no label before it."), [], graphId));
            }
        return issues;
    }

    public sealed record Palette(Color Surface, Color OnSurface, Color Button, Color OnButton, Color Field, Color Muted);

    /// <summary>The colors a design language draws with (null for Fluent, whose controls aren't restyled).</summary>
    public static Palette? Colors(AppDesign design, bool dark)
    {
        if (design.Language == "Fluent") return null;
        var palette = MaterialColorUtilities.Palettes.CorePalette.Of(Color.Parse(design.SeedColor).ToUInt32(), MaterialColorUtilities.Palettes.Style.TonalSpot);
        var d = design.Language == "Material3" ? AppDesign.Scheme(palette, dark) : DesignLooks.Look(design.Language, palette, dark);
        Color C(string key) => ((ISolidColorBrush)d[key]!).Color;
        return design.Language == "Material3"
            ? new(C("M3Surface"), C("M3OnSurface"), C("M3Primary"), C("M3OnPrimary"), C("M3SurfaceContainerHighest"), C("M3OnSurfaceVariant"))
            : new(C("FaroSurface"), C("FaroOnSurface"), C("FaroButton"), C("FaroOnButton"), C("FaroField"), C("FaroMuted"));
    }

    /// <summary>WCAG contrast ratio of a color over another (a translucent one blended onto it first).</summary>
    public static double Ratio(Color fore, Color back)
    {
        static double Channel(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        static double Luminance(double r, double g, double b) => 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
        var a = fore.A / 255.0;
        double Mix(byte f, byte b) => (f * a + b * (1 - a)) / 255.0;
        var l1 = Luminance(Mix(fore.R, back.R), Mix(fore.G, back.G), Mix(fore.B, back.B));
        var l2 = Luminance(back.R / 255.0, back.G / 255.0, back.B / 255.0);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
