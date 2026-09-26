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

    /// <summary>Every bind in the project (for project-wide follow-ups such as class renames).</summary>
    public IEnumerable<XElement> Binds => BindingFiles.SelectMany(d => d.Root!.Elements("Bind"));

    /// <summary>Bindings/&lt;ScreenId&gt;.xml belongs to that screen: its binds apply only there.</summary>
    public static string ScreenOf(XDocument bindingFile) => Path.GetFileNameWithoutExtension(new Uri(bindingFile.BaseUri).LocalPath);

    public IEnumerable<XElement> BindsFor(string screenId) =>
        BindingFiles.Where(d => ScreenOf(d) == screenId).SelectMany(d => d.Root!.Elements("Bind"));

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
        return project;
    }

    /// <summary>Saves a document back to the file it was loaded from.</summary>
    public static void Save(XDocument doc) => doc.Save(new Uri(doc.BaseUri).LocalPath);

    static IEnumerable<XDocument> LoadAll(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.xml").Order().Select(f => XDocument.Load(f, LoadOptions.SetBaseUri))
            : [];
}
