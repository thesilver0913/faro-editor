using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Themes;

namespace Faro.Editor;

/// <summary>
/// App-wide preferences in ApplicationData/Faro/settings.json (spec §14/§15: per app, not per project).
/// Never holds secrets: API keys stay in environment variables.
/// </summary>
public sealed class FaroSettings
{
    public const string BuiltInEditorTheme = "Match app theme";
    public const string CustomEditorTheme = "Custom file…";

    public string Provider { get; set; } = "Claude";
    public string ClaudeModel { get; set; } = "claude-opus-5";
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string OpenAiModel { get; set; } = "";

    public string Language { get; set; } = "en"; // "en" or "ja" (L.T)
    public bool SetupDone { get; set; } // the first-run wizard ran
    public string UpdateChannel { get; set; } = "Stable"; // Stable / Beta / Canary (Updates)
    public bool CheckUpdatesOnStart { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }
    public DateTime CrashesSeen { get; set; } // newer crash reports are offered at the next start
    public string AppTheme { get; set; } = "Dark"; // System / Dark / Light
    public string AppThemeFile { get; set; } = ""; // user ResourceDictionary (.axaml)
    public string EditorTheme { get; set => field = value == "Built-in (light)" ? BuiltInEditorTheme : value; } = BuiltInEditorTheme; // or a TextMate ThemeName, or CustomEditorTheme
    public string EditorThemeFile { get; set; } = ""; // .tmTheme or VS Code .json
    public List<string> RecentProjects { get; set; } = [];
    public List<string> PendingDeletes { get; set; } = [];
    public List<string> TrustedProjects { get; set; } = []; // workspace trust: folders whose code Faro may build and run // discarded untitled projects (ProjectSetup.DeletePending)

    // Editor layout (spec §14: saved per app). ponytail: pane sizes and the window; docking moves, tabs and floating panes reset on restart.
    public Dictionary<string, double> PaneProportions { get; set; } = [];
    public string ArtboardSize { get; set; } = "Phone"; // canvas preview width/height, see CanvasView.Sizes
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>FARO_SETTINGS overrides the location (the self-checks use a scratch file, never the user's settings).</summary>
    public static string FilePath => Environment.GetEnvironmentVariable("FARO_SETTINGS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Faro", "settings.json");

    public static FaroSettings Current { get; } = Load();

    /// <summary>Raised after theme settings change so open editors re-theme themselves.</summary>
    public static event Action? ThemeChanged;

    static FaroSettings Load()
    {
        try { return JsonSerializer.Deserialize<FaroSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp"; // written whole, then swapped in: a crash mid-save can't lose the settings (trusted projects, recents)
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, FilePath, overwrite: true);
    }

    public IChatProvider CreateProvider() => Provider == "Claude"
        ? new ClaudeProvider(ClaudeModel)
        : new OpenAiCompatibleProvider(OpenAiBaseUrl, OpenAiModel);

    static ResourceDictionary? appliedThemeFile;

    /// <summary>
    /// Applies the app theme: variant plus an optional user AXAML ResourceDictionary (spec §15), then tells
    /// editors to reload their colors. Returns an error message instead of throwing on a bad theme file.
    /// </summary>
    public string? ApplyTheme()
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = AppTheme switch { "Light" => ThemeVariant.Light, "System" => ThemeVariant.Default, _ => ThemeVariant.Dark };
        if (appliedThemeFile is not null) app.Resources.MergedDictionaries.Remove(appliedThemeFile);
        appliedThemeFile = null;
        string? error = null;
        if (AppThemeFile.Length > 0)
        {
            try { app.Resources.MergedDictionaries.Add(appliedThemeFile = LoadThemeFile(AppThemeFile)); }
            catch (Exception e) { error = $"App theme file: {e.Message}"; }
        }
        ThemeChanged?.Invoke();
        return error;
    }

    /// <summary>A user theme file: an Avalonia ResourceDictionary in AXAML.</summary>
    public static ResourceDictionary LoadThemeFile(string path) =>
        AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)) as ResourceDictionary
            ?? throw new InvalidDataException("The root element must be a ResourceDictionary.");

    /// <summary>The TextMate theme for the code editor, or null for the built-in .xshd colors (light). The default follows the app: Dark+ when dark.</summary>
    public IRawTheme? LoadEditorTheme()
    {
        if (EditorTheme == BuiltInEditorTheme)
            return Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? new RegistryOptions(ThemeName.DarkPlus).LoadTheme(ThemeName.DarkPlus) : null;
        if (EditorTheme != CustomEditorTheme) return new RegistryOptions(Enum.Parse<ThemeName>(EditorTheme)).LoadTheme(Enum.Parse<ThemeName>(EditorTheme));
        var text = File.ReadAllText(EditorThemeFile);
        var json = text.TrimStart().StartsWith('<') ? TmThemeToJson(text) : text; // TextMateSharp only reads JSON
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        return ThemeReader.ReadThemeSync(reader);
    }

    /// <summary>Converts an XML plist (.tmTheme) to the equivalent JSON.</summary>
    public static string TmThemeToJson(string plist)
    {
        static JsonNode? Convert(XElement e) => e.Name.LocalName switch
        {
            "dict" => new JsonObject(e.Elements().Where(k => k.Name == "key").Select(k => KeyValuePair.Create(k.Value, Convert((XElement)k.NextNode!)))),
            "array" => new JsonArray([.. e.Elements().Select(Convert)]),
            "true" => true,
            "false" => false,
            "integer" => long.Parse(e.Value),
            "real" => double.Parse(e.Value, System.Globalization.CultureInfo.InvariantCulture),
            _ => e.Value,
        };
        return Convert(XDocument.Parse(plist, LoadOptions.None).Root!.Elements().First())!.ToJsonString();
    }
}
