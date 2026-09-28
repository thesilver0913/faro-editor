using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using MaterialColorUtilities.Palettes;

namespace Faro.Runtime;

/// <summary>
/// The app's design language, from faro.json's "design": { "language": "Material3", "seedColor": "#6750A4", "theme": "Light" }.
/// Fluent (default) is Avalonia's own look. Material 3 Expressive adds color roles generated from the seed color
/// (light and dark), its shapes, type scale and springy press morph (Material3.axaml). Nodes pick per-language
/// options with attributes such as m3.variant="Tonal"; UiBuilder turns them into style classes ("m3-variant-tonal"),
/// so a language that doesn't know them simply ignores them.
/// </summary>
public sealed record AppDesign(string Language = "Fluent", string SeedColor = AppDesign.DefaultSeed, string Theme = "System")
{
    public const string DefaultSeed = "#6750A4";
    public static readonly string[] Languages = ["Fluent", "Material3"];
    public static readonly string[] Themes = ["System", "Light", "Dark"];

    public static AppDesign Read(JsonNode? design) => new(
        (string?)design?["language"] is { } language && Languages.Contains(language) ? language : "Fluent",
        (string?)design?["seedColor"] is { } seed && Color.TryParse(seed, out _) ? seed : DefaultSeed,
        (string?)design?["theme"] is { } theme && Themes.Contains(theme) ? theme : "System");

    public ThemeVariant Variant => Theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };

    /// <summary>Adds the language's styles and colors to a host: the app, or the editor's artboard (a ThemeVariantScope).</summary>
    public void Apply(Styles styles, IResourceDictionary resources)
    {
        if (Language != "Material3") return;
        var palette = CorePalette.Of(Color.Parse(SeedColor).ToUInt32(), MaterialColorUtilities.Palettes.Style.TonalSpot);
        resources.ThemeDictionaries[ThemeVariant.Light] = Scheme(palette, dark: false);
        resources.ThemeDictionaries[ThemeVariant.Dark] = Scheme(palette, dark: true);
        // Text fields read their colors and border widths from resources inside their templates (Fluent and FluentAvalonia),
        // so the variants swap those: filled (default) is a tinted field with a bottom indicator, outlined a 1px outline.
        var outlined = new Avalonia.Styling.Style(x => x.OfType<TextBox>().Class("m3-variant-outlined"));
        outlined.Resources.ThemeDictionaries[ThemeVariant.Light] = TextField(palette, dark: false, outlined: true);
        outlined.Resources.ThemeDictionaries[ThemeVariant.Dark] = TextField(palette, dark: true, outlined: true);
        styles.Add(outlined);
        styles.Add(new StyleInclude(new Uri("avares://Faro.Runtime/")) { Source = new Uri("avares://Faro.Runtime/Material3.axaml") });
    }

    /// <summary>M3 color roles as brushes "M3Primary", "M3OnSurface", … (tones from the M3 spec), plus Fluent's accent for unstyled controls.</summary>
    public static ResourceDictionary Scheme(CorePalette p, bool dark)
    {
        var roles = new Dictionary<string, (TonalPalette Palette, uint Light, uint Dark)>
        {
            ["Primary"] = (p.Primary, 40, 80), ["OnPrimary"] = (p.Primary, 100, 20), ["PrimaryContainer"] = (p.Primary, 90, 30), ["OnPrimaryContainer"] = (p.Primary, 10, 90),
            ["Secondary"] = (p.Secondary, 40, 80), ["OnSecondary"] = (p.Secondary, 100, 20), ["SecondaryContainer"] = (p.Secondary, 90, 30), ["OnSecondaryContainer"] = (p.Secondary, 10, 90),
            ["Tertiary"] = (p.Tertiary, 40, 80), ["OnTertiary"] = (p.Tertiary, 100, 20), ["TertiaryContainer"] = (p.Tertiary, 90, 30), ["OnTertiaryContainer"] = (p.Tertiary, 10, 90),
            ["Error"] = (p.Error, 40, 80), ["OnError"] = (p.Error, 100, 20), ["ErrorContainer"] = (p.Error, 90, 30), ["OnErrorContainer"] = (p.Error, 10, 90),
            ["Surface"] = (p.Neutral, 98, 6), ["OnSurface"] = (p.Neutral, 10, 90), ["OnSurfaceVariant"] = (p.NeutralVariant, 30, 80),
            ["SurfaceContainerLowest"] = (p.Neutral, 100, 4), ["SurfaceContainerLow"] = (p.Neutral, 96, 10), ["SurfaceContainer"] = (p.Neutral, 94, 12),
            ["SurfaceContainerHigh"] = (p.Neutral, 92, 17), ["SurfaceContainerHighest"] = (p.Neutral, 90, 22),
            ["Outline"] = (p.NeutralVariant, 50, 60), ["OutlineVariant"] = (p.NeutralVariant, 80, 30),
            ["InverseSurface"] = (p.Neutral, 20, 90), ["InverseOnSurface"] = (p.Neutral, 95, 20), ["InversePrimary"] = (p.Primary, 80, 40),
        };
        var dictionary = new ResourceDictionary();
        foreach (var (role, (palette, light, darkTone)) in roles)
            dictionary["M3" + role] = new SolidColorBrush(Color.FromUInt32(palette.Tone(dark ? darkTone : light)));
        var primary = Color.FromUInt32(p.Primary.Tone(dark ? 80u : 40u));
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorLight1" }) dictionary[key] = primary;
        foreach (var (key, value) in TextField(p, dark, outlined: false)) dictionary[key] = value;
        return dictionary;
    }

    /// <summary>The TextControl* resources Fluent's text box template uses, as an M3 filled or outlined text field.</summary>
    static ResourceDictionary TextField(CorePalette p, bool dark, bool outlined)
    {
        IBrush Tone(TonalPalette palette, uint light, uint darkTone) => new SolidColorBrush(Color.FromUInt32(palette.Tone(dark ? darkTone : light)));
        var field = outlined ? Brushes.Transparent : Tone(p.Neutral, 90, 22); // surface container highest
        var line = outlined ? Tone(p.NeutralVariant, 50, 60) : Tone(p.NeutralVariant, 30, 80); // outline / on surface variant
        var focus = Tone(p.Primary, 40, 80);
        var text = Tone(p.Neutral, 10, 90);
        var hint = Tone(p.NeutralVariant, 30, 80);
        var dictionary = new ResourceDictionary
        {
            ["TextControlBorderThemeThickness"] = outlined ? new Thickness(1) : new Thickness(0, 0, 0, 1),
            ["TextControlBorderThemeThicknessFocused"] = outlined ? new Thickness(2) : new Thickness(0, 0, 0, 2),
            ["TextControlBorderBrushFocused"] = focus, ["TextControlElevationBorderFocusedBrush"] = focus,
            ["TextControlBorderBrush"] = line, ["TextControlBorderBrushPointerOver"] = text, ["TextControlElevationBorderBrush"] = line,
            ["TextControlSelectionHighlightColor"] = focus,
        };
        foreach (var state in new[] { "", "PointerOver", "Focused" })
        {
            dictionary["TextControlBackground" + state] = field;
            dictionary["TextControlForeground" + state] = text;
            dictionary["TextControlPlaceholderForeground" + state] = hint;
        }
        return dictionary;
    }
}
