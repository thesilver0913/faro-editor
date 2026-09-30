using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Faro.Runtime;

/// <summary>
/// A Faro project folder (spec §4): UI/ holds one UIGraph or ComponentDef per XML file,
/// Bindings/ holds &lt;Bindings&gt; files, Source/ the C# code, Assets/ the images.
/// Documents are kept as XDocument so the editor can edit and save them losslessly.
/// </summary>
public sealed class FaroProject
{
    public required string Root { get; init; }
    public Dictionary<string, XDocument> Screens { get; } = [];
    public Dictionary<string, XDocument> Components { get; } = [];
    public List<XDocument> BindingFiles { get; } = [];

    /// <summary>"startScreen" in faro.json: the screen the app opens with (kept out of the code, so it's language-neutral).</summary>
    public string? StartScreen { get; private set; }

    /// <summary>"design" in faro.json: the app's design language, seed color and light/dark theme.</summary>
    public AppDesign Design { get; private set; } = new();

    /// <summary>"tokens" in faro.json: named sizes ("space.m": 16) that number attributes use as "$space.m", so one change restyles every screen.</summary>
    public Dictionary<string, string> Tokens { get; private set; } = [];

    /// <summary>faro.json "locale" ("ja-JP"): dates and numbers in that language; null follows the device.</summary>
    public string? Locale { get; private set; }

    /// <summary>Every bind in the project (for project-wide follow-ups such as class renames).</summary>
    public IEnumerable<XElement> Binds => BindingFiles.SelectMany(d => d.Root!.Elements("Bind"));

    /// <summary>Bindings/&lt;Id&gt;.xml belongs to the screen or component with that id: its binds apply only there.</summary>
    public static string ScreenOf(XDocument bindingFile) => Path.GetFileNameWithoutExtension(new Uri(bindingFile.BaseUri).LocalPath);

    /// <summary>A screen or a component master by id (both are edited on the canvas).</summary>
    public XDocument? Graph(string id) => Screens.GetValueOrDefault(id) ?? Components.GetValueOrDefault(id);

    /// <summary>Binds of a screen, or of a component master (those apply inside every instance, spec §5; a variant "Comp.X@Outlined" shares Comp.X's).</summary>
    public IEnumerable<XElement> BindsFor(string screenId) =>
        BindingFiles.Where(d => ScreenOf(d) == screenId.Split('@')[0]).SelectMany(d => d.Root!.Elements("Bind"));

    public static FaroProject Load(string root)
    {
        var project = new FaroProject { Root = root };
        foreach (var doc in LoadAll(Path.Combine(root, "UI")))
        {
            var id = (string?)doc.Root!.Attribute("id") ?? throw new InvalidDataException($"{doc.BaseUri}: root has no id");
            _ = doc.Root.Name.LocalName switch
            {
                "UIGraph" => project.Screens[id] = doc,
                "ComponentDef" => project.Components[id] = doc,
                var other => throw new InvalidDataException($"{doc.BaseUri}: unexpected root <{other}>"),
            };
        }
        project.BindingFiles.AddRange(LoadAll(Path.Combine(root, "Bindings")));
        var meta = Path.Combine(root, "faro.json");
        JsonNode? json;
        try { json = File.Exists(meta) ? JsonNode.Parse(File.ReadAllText(meta)) : null; }
        catch (System.Text.Json.JsonException e) { throw new InvalidDataException($"faro.json: {e.Message}", e); } // hand-edited: say where
        project.StartScreen = (string?)json?["startScreen"];
        project.Locale = (string?)json?["locale"];
        project.Design = AppDesign.Read(json?["design"]);
        project.Tokens = json?["tokens"] is JsonObject tokens ? tokens.ToDictionary(t => t.Key, t => t.Value is JsonValue v && v.TryGetValue<string>(out var text) ? text : t.Value?.ToString() ?? "") : [];
        UiBuilder.Tokens = project.Tokens; // every build follows a load
        return project;
    }

    /// <summary>Saves a document back to the file it was loaded from.</summary>
    public static void Save(XDocument doc) => doc.Save(new Uri(doc.BaseUri).LocalPath);

    static IEnumerable<XDocument> LoadAll(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.xml").Order().Select(f => XDocument.Load(f, LoadOptions.SetBaseUri))
            : [];
}
