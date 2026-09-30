using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using MaterialColorUtilities.Palettes;

namespace Faro.Runtime;

/// <summary>
/// The values behind Looks.axaml for the design languages other than Fluent and Material 3, per theme. Colors come
/// from the seed color's tonal palettes (as Material 3's do), so every language follows the project's color.
/// The same table writes the Java runtime's CSS (JavaProject.DesignCss), so both apps look alike.
/// </summary>
public static class DesignLooks
{
    public static readonly string[] Languages = ["Cupertino", "Neumorphism", "NeoBrutalism", "Simple", "Carbon", "Clay", "Retro"];

    public static ResourceDictionary Look(string language, CorePalette p, bool dark)
    {
        Color Tone(TonalPalette palette, uint light, uint darkTone) => Color.FromUInt32(palette.Tone(dark ? darkTone : light));
        Color Hex(string light, string darkHex) => Color.Parse(dark ? darkHex : light);
        var accent = Tone(p.Primary, 40, 80);
        var onAccent = Tone(p.Primary, 100, 20);
        var v = new Dictionary<string, object>();
        void Palette(Color surface, Color onSurface, Color muted, Color field, Color border, Color track, Color knob) =>
            (v["FaroSurface"], v["FaroOnSurface"], v["FaroMuted"], v["FaroField"], v["FaroBorder"], v["FaroTrack"], v["FaroKnob"]) = (surface, onSurface, muted, field, border, track, knob);
        void Sizes(double button, double field, double check, double switchWidth, double switchHeight, double knob, double sliderTrack, double thumbWidth, double thumbHeight, double progress) =>
            (v["FaroButtonHeight"], v["FaroFieldHeight"], v["FaroCheckSize"], v["FaroSwitchWidth"], v["FaroSwitchHeight"], v["FaroKnobSize"], v["FaroSliderTrack"], v["FaroThumbWidth"], v["FaroThumbHeight"], v["FaroProgressHeight"])
                = (button, field, check, switchWidth, switchHeight, knob, sliderTrack, thumbWidth, thumbHeight, progress);
        void Corners(double button, double field, double check, double track, double thumb, double knob) =>
            (v["FaroButtonRadius"], v["FaroFieldRadius"], v["FaroCheckRadius"], v["FaroTrackRadius"], v["FaroThumbRadius"], v["FaroKnobRadius"]) = (button, field, check, track, thumb, knob);
        void Borders(double button, double field, double check, double track, double knob, double thumb) =>
            (v["FaroButtonBorderThickness"], v["FaroFieldBorderThickness"], v["FaroCheckBorderThickness"], v["FaroTrackBorderThickness"], v["FaroKnobBorderThickness"], v["FaroThumbBorderThickness"]) = (button, field, check, track, knob, thumb);
        void Shadows(string button, string pressed, string field, string check, string track, string knob) =>
            (v["FaroButtonShadow"], v["FaroButtonPressedShadow"], v["FaroFieldShadow"], v["FaroCheckShadow"], v["FaroTrackShadow"], v["FaroKnobShadow"]) = (button, pressed, field, check, track, knob);
        // Per-node options (look.surface="Card" / "Inset" on a container)
        void Cards(Color card, double radius, double border, string shadow, Color inset, string insetShadow) =>
            (v["FaroCard"], v["FaroCardRadius"], v["FaroCardBorderThickness"], v["FaroCardShadow"], v["FaroInset"], v["FaroInsetShadow"]) = (card, radius, border, shadow, inset, insetShadow);
        (v["FaroTonal"], v["FaroOnTonal"]) = (Tone(p.Secondary, 90, 30), Tone(p.Secondary, 10, 90)); // look.variant="Tonal"
        v["FaroAccentText"] = accent; // look.variant="Outlined" / "Text"
        v["FaroAccent"] = accent;
        v["FaroOnAccent"] = onAccent;
        (v["FaroButton"], v["FaroOnButton"]) = (accent, onAccent);
        v["FaroButtonWeight"] = FontWeight.SemiBold;
        v["FaroPressedOffset"] = "none";

        switch (language)
        {
            // Apple's iOS controls: grouped gray background, white fields and cells, a capsule switch with a shadowed white knob.
            case "Cupertino":
                Palette(Hex("#F2F2F7", "#000000"), Hex("#000000", "#FFFFFF"), Hex("#8E8E93", "#8E8E93"), Hex("#FFFFFF", "#1C1C1E"),
                    Hex("#C6C6C8", "#38383A"), Hex("#E9E9EA", "#39393D"), Colors.White);
                Sizes(44, 44, 22, 51, 31, 27, 4, 28, 28, 4);
                Corners(12, 10, 11, 16, 14, 14);
                Borders(0, 0, 1.5, 0, 0, 0);
                Shadows("none", "none", "none", "none", "none", "0 3 8 0 #26000000, 0 3 1 0 #0F000000");
                Cards(Hex("#FFFFFF", "#1C1C1E"), 12, 0, "none", Hex("#E5E5EA", "#2C2C2E"), "none");
                break;
            // Soft UI: controls pressed out of (or into) the surface by a light and a dark shadow. Text and checked states keep
            // full contrast (dark text, accent fills), which plain neumorphism loses.
            case "Neumorphism":
                var surface = Tone(p.Neutral, 92, 17);
                var shade = dark ? "#99000000" : "#59" + (Tone(p.NeutralVariant, 60, 60).ToUInt32() & 0xFFFFFF).ToString("X6");
                var light = dark ? "#1FFFFFFF" : "#E6FFFFFF";
                Palette(surface, Tone(p.Neutral, 20, 90), Tone(p.NeutralVariant, 40, 70), surface, Colors.Transparent, surface, surface);
                (v["FaroButton"], v["FaroOnButton"]) = (surface, accent);
                Sizes(48, 48, 24, 56, 30, 22, 10, 24, 24, 10);
                Corners(16, 14, 7, 15, 12, 11);
                Borders(0, 0, 0, 0, 0, 0);
                var raised = $"5 5 10 0 {shade}, -5 -5 10 0 {light}";
                var sunk = $"inset 4 4 8 0 {shade}, inset -4 -4 8 0 {light}";
                Shadows(raised, sunk, sunk, $"3 3 6 0 {shade}, -3 -3 6 0 {light}", $"inset 2 2 5 0 {shade}, inset -2 -2 5 0 {light}", $"3 3 6 0 {shade}, -2 -2 5 0 {light}");
                Cards(surface, 20, 0, $"8 8 16 0 {shade}, -8 -8 16 0 {light}", surface, $"inset 6 6 12 0 {shade}, inset -6 -6 12 0 {light}");
                break;
            // Neo-brutalism: flat bright color, thick dark outlines and hard offset shadows; a pressed button moves onto its shadow.
            case "NeoBrutalism":
                var ink = Hex("#000000", "#F5F5F5");
                Palette(Tone(p.Tertiary, 95, 10), ink, Tone(p.Neutral, 30, 80), Hex("#FFFFFF", "#1A1A1A"), ink, Hex("#FFFFFF", "#1A1A1A"), Hex("#FFFFFF", "#1A1A1A"));
                v["FaroAccent"] = Tone(p.Primary, 80, 70);
                v["FaroOnAccent"] = Hex("#000000", "#000000");
                (v["FaroButton"], v["FaroOnButton"]) = (v["FaroAccent"], v["FaroOnAccent"]);
                v["FaroButtonWeight"] = FontWeight.Bold;
                Sizes(46, 46, 24, 56, 30, 20, 14, 22, 30, 18);
                Corners(8, 8, 6, 8, 6, 6);
                Borders(2, 2, 2, 2, 2, 2);
                var hard = $"4 4 0 0 {(dark ? "#F5F5F5" : "#000000")}";
                Shadows(hard, "none", $"3 3 0 0 {(dark ? "#F5F5F5" : "#000000")}", "2 2 0 0 " + (dark ? "#F5F5F5" : "#000000"), "none", "none");
                v["FaroPressedOffset"] = "translate(4px, 4px)";
                v["FaroFont"] = "avares://Faro.Runtime/Assets/Fonts#Google Sans Flex";
                Cards(Hex("#FFFFFF", "#1A1A1A"), 10, 2, hard, Tone(p.Tertiary, 88, 20), "none");
                (v["FaroTonal"], v["FaroOnTonal"]) = (Tone(p.Secondary, 85, 75), Hex("#000000", "#000000"));
                v["FaroAccentText"] = ink;
                break;
            // IBM's Carbon: square corners, fields as filled boxes with a bottom rule, gray layers, no shadows.
            case "Carbon":
                Palette(Hex("#FFFFFF", "#161616"), Hex("#161616", "#F4F4F4"), Hex("#6F6F6F", "#A8A8A8"), Hex("#F4F4F4", "#262626"),
                    Hex("#8D8D8D", "#6F6F6F"), Hex("#C6C6C6", "#525252"), Hex("#FFFFFF", "#F4F4F4"));
                v["FaroButtonWeight"] = FontWeight.Normal;
                Sizes(48, 40, 16, 48, 24, 18, 2, 14, 14, 8);
                Corners(0, 0, 1, 12, 7, 9);
                Borders(0, 0, 1, 0, 0, 0);
                v["FaroFieldBorderThickness"] = new Thickness(0, 0, 0, 1);
                Shadows("none", "none", "none", "none", "none", "none");
                Cards(Hex("#F4F4F4", "#262626"), 0, 0, "none", Hex("#E0E0E0", "#393939"), "none");
                (v["FaroTonal"], v["FaroOnTonal"]) = (Hex("#393939", "#6F6F6F"), Hex("#FFFFFF", "#FFFFFF")); // Carbon's secondary button
                v["FaroThumb"] = accent;
                break;
            // Claymorphism: puffy pastel shapes, a soft drop shadow with an inner highlight and shade, round everything.
            case "Clay":
                var clayShade = $"#40{Tone(p.Primary, 30, 0).ToUInt32() & 0xFFFFFF:X6}";
                var clayInner = $"#33{Tone(p.Primary, 30, 0).ToUInt32() & 0xFFFFFF:X6}";
                var gloss = dark ? "#26FFFFFF" : "#B3FFFFFF";
                Palette(Tone(p.Primary, 94, 12), Tone(p.Neutral, 15, 90), Tone(p.NeutralVariant, 40, 70), Tone(p.Neutral, 99, 22),
                    Colors.Transparent, Tone(p.Primary, 86, 25), Tone(p.Neutral, 99, 90));
                v["FaroAccent"] = Tone(p.Primary, 55, 75);
                v["FaroOnAccent"] = Tone(p.Primary, 100, 10);
                (v["FaroButton"], v["FaroOnButton"]) = (v["FaroAccent"], v["FaroOnAccent"]);
                v["FaroButtonWeight"] = FontWeight.Bold;
                Sizes(52, 52, 26, 60, 34, 26, 14, 28, 28, 14);
                Corners(26, 22, 9, 17, 14, 13);
                Borders(0, 0, 0, 0, 0, 0);
                var clay = $"8 8 16 0 {clayShade}, inset -4 -4 8 0 {clayInner}, inset 4 4 8 0 {gloss}";
                var claySunk = $"inset 4 4 8 0 {clayShade}, inset -4 -4 8 0 {gloss}";
                Shadows(clay, $"3 3 8 0 {clayShade}, inset -2 -2 6 0 {clayInner}, inset 3 3 6 0 {gloss}", claySunk,
                    $"4 4 8 0 {clayShade}, inset 2 2 4 0 {gloss}", claySunk, $"4 4 8 0 {clayShade}, inset -2 -2 4 0 {clayInner}, inset 2 2 4 0 {gloss}");
                v["FaroFont"] = "avares://Faro.Runtime/Assets/Fonts#Google Sans Flex";
                Cards(Tone(p.Neutral, 99, 22), 28, 0, $"12 12 24 0 {clayShade}, inset -6 -6 12 0 {clayInner}, inset 6 6 12 0 {gloss}", Tone(p.Primary, 90, 16), claySunk);
                break;
            // Windows 95: a gray desktop of beveled boxes (light top-left, dark bottom-right; reversed when sunk or pressed).
            case "Retro":
                var (hi, lo, face) = (Hex("#FFFFFF", "#6A6A6A"), Hex("#808080", "#1A1A1A"), Hex("#C0C0C0", "#3C3C3C"));
                string Bevel(Color topLeft, Color bottomRight) => $"inset 1 1 0 0 {topLeft}, inset -1 -1 0 0 {bottomRight}";
                Palette(face, Hex("#000000", "#E0E0E0"), Hex("#808080", "#9A9A9A"), Hex("#FFFFFF", "#1E1E1E"), Hex("#000000", "#000000"), Hex("#FFFFFF", "#1E1E1E"), face);
                v["FaroAccent"] = Tone(p.Primary, 30, 80);
                v["FaroOnAccent"] = Tone(p.Primary, 100, 10);
                (v["FaroButton"], v["FaroOnButton"]) = (face, v["FaroOnSurface"]);
                (v["FaroTonal"], v["FaroOnTonal"]) = (v["FaroAccent"], v["FaroOnAccent"]); // the title bar's color
                v["FaroButtonWeight"] = FontWeight.Normal;
                Sizes(30, 28, 16, 44, 22, 16, 6, 12, 22, 22);
                Corners(0, 0, 0, 0, 0, 0);
                Borders(1, 0, 0, 0, 0, 0);
                var sunkBevel = $"{Bevel(lo, hi)}, inset 2 2 0 0 {Hex("#404040", "#000000")}";
                Shadows(Bevel(hi, lo), sunkBevel, sunkBevel, sunkBevel, sunkBevel, Bevel(hi, lo));
                Cards(face, 0, 0, Bevel(hi, lo), Hex("#FFFFFF", "#1E1E1E"), sunkBevel);
                break;
            // A quiet, neutral kit in the style of shadcn/ui: hairline borders, small corners, a faint shadow.
            case "Simple":
            default:
                Palette(Hex("#FFFFFF", "#09090B"), Hex("#09090B", "#FAFAFA"), Hex("#71717A", "#A1A1AA"), Hex("#FFFFFF", "#09090B"),
                    Hex("#E4E4E7", "#27272A"), Hex("#F4F4F5", "#27272A"), Hex("#FFFFFF", "#09090B"));
                v["FaroButtonWeight"] = FontWeight.Medium;
                Sizes(36, 36, 16, 36, 20, 16, 6, 16, 16, 8);
                Corners(6, 6, 4, 10, 8, 8);
                Borders(0, 1, 1, 0, 0, 1);
                Shadows("0 1 2 0 #0D000000", "none", "0 1 2 0 #0D000000", "none", "none", "0 1 2 0 #1A000000");
                Cards(Hex("#FFFFFF", "#09090B"), 8, 1, "0 1 3 0 #1A000000", Hex("#F4F4F5", "#18181B"), "none");
                (v["FaroTonal"], v["FaroOnTonal"]) = (Hex("#F4F4F5", "#27272A"), Hex("#18181B", "#FAFAFA")); // shadcn's secondary
                break;
        }
        v.TryAdd("FaroThumb", v["FaroKnob"]); // the slider's handle
        v["FaroCheckBorder"] = language == "Simple" ? accent : v["FaroBorder"] is Color b && b.A > 0 ? b : v["FaroMuted"];
        v["FaroCheckedBorder"] = language == "NeoBrutalism" ? v["FaroBorder"] : v["FaroAccent"];
        v["FaroThumbBorder"] = language == "Simple" ? accent : v["FaroBorder"];
        v["FaroFocus"] = language == "NeoBrutalism" ? v["FaroBorder"] : v["FaroAccent"]; // a focused field's outline
        // look.variant="Outlined": the language's visible border (or its muted text color where borders are invisible)
        v["FaroOutline"] = v["FaroBorder"] is Color o && o.A > 0 ? o : v["FaroMuted"];
        v["FaroOutlineThickness"] = Math.Max(1.0, v["FaroButtonBorderThickness"] is double bt ? bt : 1);

        var d = new ResourceDictionary();
        foreach (var (key, value) in v)
            d[key] = key switch
            {
                _ when value is Color c => new SolidColorBrush(c),
                _ when key.EndsWith("Shadow") => BoxShadows.Parse((string)value),
                _ when key.EndsWith("Radius") => new CornerRadius((double)value),
                _ when key.EndsWith("Thickness") => value as Thickness? ?? new Thickness((double)value),
                "FaroPressedOffset" => TransformOperations.Parse((string)value),
                "FaroFont" => new FontFamily((string)value),
                _ => value,
            };
        var height = (double)v["FaroSwitchHeight"];
        var knob = (double)v["FaroKnobSize"];
        d["FaroKnobInset"] = new Thickness((height - knob) / 2, 0, 0, 0);
        d["FaroKnobTravel"] = (double)v["FaroSwitchWidth"] - knob - (height - knob);

        // Fluent's text boxes and combo boxes (their templates stay; the colors and borders are the language's)
        var field = d["FaroField"];
        var fieldBorder = (Thickness)d["FaroFieldBorderThickness"];
        foreach (var state in new[] { "", "PointerOver", "Focused" })
        {
            d["TextControlBackground" + state] = field;
            d["TextControlForeground" + state] = d["FaroOnSurface"];
            d["TextControlPlaceholderForeground" + state] = d["FaroMuted"];
        }
        d["TextControlBorderBrush"] = d["TextControlBorderBrushPointerOver"] = d["TextControlElevationBorderBrush"] = d["FaroBorder"];
        d["TextControlBorderBrushFocused"] = d["TextControlElevationBorderFocusedBrush"] = d["FaroFocus"];
        d["TextControlBorderThemeThickness"] = d["TextControlBorderThemeThicknessFocused"] = fieldBorder;
        d["TextControlSelectionHighlightColor"] = d["FaroAccent"];
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Unfocused" }) d["ComboBoxBackground" + state] = field;
        foreach (var state in new[] { "", "PointerOver", "Pressed" }) d["ComboBoxBorderBrush" + state] = d["FaroBorder"];
        foreach (var state in new[] { "", "Focused", "FocusedPressed" })
        {
            d["ComboBoxForeground" + state] = d["FaroOnSurface"];
            d["ComboBoxDropDownGlyphForeground" + state] = d["FaroMuted"];
        }
        d["ComboBoxBorderThemeThickness"] = fieldBorder;
        d["ComboBoxBackgroundBorderBrushFocused"] = d["FaroFocus"];
        d["ComboBoxMinHeight"] = v["FaroFieldHeight"];
        d["ComboBoxPadding"] = new Thickness(12, 0, 8, 0);
        d["ComboBoxDropDownBackground"] = field;
        d["ComboBoxDropDownBorderBrush"] = d["FaroBorder"];
        d["ComboBoxItemThemePadding"] = new Thickness(12, 0);
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            d["ComboBoxItemForeground" + state] = d["FaroOnSurface"];
            d["ComboBoxItemBackgroundSelected" + state] = d["FaroTrack"];
            d["ComboBoxItemForegroundSelected" + state] = d["FaroOnSurface"];
        }
        d["ComboBoxItemBackgroundPointerOver"] = d["ComboBoxItemBackgroundPressed"] = d["FaroTrack"];
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorLight1" }) d[key] = (Color)v["FaroAccent"];
        d["FaroDivider"] = d["FaroBorder"] is SolidColorBrush { Color.A: > 0 } ? d["FaroBorder"] : new SolidColorBrush(Tone(p.NeutralVariant, 80, 30));
        return d;
    }
}
