using System.Xml.Linq;
using Faro.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Faro.Editor;

/// <summary>A bindable public member found in Source/. <c>Target</c> is the "Ns.Class.Member" string used by &lt;Bind&gt;.</summary>
public sealed record RegistryMember(string Target, bool IsMethod, string Signature);

public sealed record BindingIssue(string NodeId, string Target, string Message, IReadOnlyList<string> Suggestions);

public static class Registry
{
    /// <summary>
    /// Extracts public members of public top-level classes with Roslyn syntax trees only,
    /// so it works on code that has never been built (spec §3).
    /// </summary>
    // ponytail: full re-parse of Source/ on every change; incremental parsing when projects get big (spec §11.5)
    public static List<RegistryMember> Scan(string sourceDir) =>
        !Directory.Exists(sourceDir) ? [] :
        [.. from file in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            from cls in CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            where cls.Parent is not TypeDeclarationSyntax && IsPublic(cls.Modifiers)
            let ns = string.Join('.', cls.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()))
            let fullName = ns.Length > 0 ? $"{ns}.{cls.Identifier}" : cls.Identifier.Text
            from member in cls.Members
            where IsPublic(member.Modifiers)
            let entry = member switch
            {
                MethodDeclarationSyntax m => new RegistryMember($"{fullName}.{m.Identifier}", true, $"{m.ReturnType} {m.Identifier}{m.ParameterList}"),
                PropertyDeclarationSyntax p => new RegistryMember($"{fullName}.{p.Identifier}", false, $"{p.Type} {p.Identifier}"),
                _ => null,
            }
            where entry is not null
            select entry];

    static bool IsPublic(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.PublicKeyword);
}

public static class BindingCheck
{
    /// <summary>Finds broken bindings (red badges, spec §6) with up to 3 near-name suggestions each.</summary>
    public static List<BindingIssue> Check(FaroProject project, List<RegistryMember> registry)
    {
        var nodes = ScreenNodes(project).ToList();
        var nodeIds = nodes.Select(n => (string?)n.Attribute("id")).ToHashSet();
        var issues = new List<BindingIssue>();

        foreach (var n in nodes.Where(n => (string?)n.Attribute("type") == "Instance" && !project.Components.ContainsKey((string?)n.Attribute("component") ?? "")))
            issues.Add(new((string)n.Attribute("id")!, "", $"Component '{(string?)n.Attribute("component")}' does not exist.", Nearest((string?)n.Attribute("component") ?? "", project.Components.Keys)));

        foreach (var bind in project.Binds)
        {
            var nodeId = (string?)bind.Attribute("nodeId") ?? "";
            var target = (string?)bind.Attribute("target") ?? "";
            if (!nodeIds.Contains(nodeId))
                issues.Add(new(nodeId, target, $"Node '{nodeId}' does not exist.", []));
            else if (target.StartsWith("Navigate:"))
            {
                var screen = FaroApp.NavigateScreenId(target, project.Screens.Keys);
                if (!project.Screens.ContainsKey(screen))
                    issues.Add(new(nodeId, target, $"Screen '{screen}' does not exist.", Nearest(screen, project.Screens.Keys).Select(s => $"Navigate:Screen.{s}").ToList()));
            }
            else
            {
                var isEvent = bind.Attribute("event") is not null;
                if (!registry.Any(m => m.Target == target && m.IsMethod == isEvent))
                    issues.Add(new(nodeId, target, $"{(isEvent ? "Method" : "Property")} '{target}' not found in Source/.",
                        Nearest(target, registry.Where(m => m.IsMethod == isEvent).Select(m => m.Target))));
            }
        }
        return issues;
    }

    /// <summary>Nodes authored in screens, excluding the master snapshots stored inside instances.</summary>
    public static IEnumerable<XElement> ScreenNodes(FaroProject project) =>
        project.Screens.Values.SelectMany(d => d.Descendants("Node"))
            .Where(n => !n.Ancestors("Node").Any(a => (string?)a.Attribute("type") == "Instance"));

    static List<string> Nearest(string name, IEnumerable<string> candidates) =>
        [.. candidates.OrderBy(c => Distance(name, c)).Take(3)];

    /// <summary>Levenshtein distance.</summary>
    public static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var diag = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
                (diag, row[j]) = (row[j], Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diag + (a[i - 1] == b[j - 1] ? 0 : 1)));
        }
        return row[b.Length];
    }
}

/// <summary>
/// Explicit master→instance sync (spec §5, §11.5): each Instance stores a snapshot of its
/// ComponentDef's root Node, which only changes when <see cref="Sync"/> is run.
/// </summary>
public static class ComponentSync
{
    public static List<XElement> OutOfDate(FaroProject project) =>
        [.. from n in BindingCheck.ScreenNodes(project)
            where (string?)n.Attribute("type") == "Instance"
            let master = project.Components.GetValueOrDefault((string?)n.Attribute("component") ?? "")?.Root!.Element("Node")
            where master is not null && !XNode.DeepEquals(n.Element("Node"), master)
            select n];

    /// <summary>Replaces the snapshots of out-of-date instances and returns the documents to save.</summary>
    public static List<XDocument> Sync(FaroProject project)
    {
        var changed = OutOfDate(project);
        foreach (var n in changed)
        {
            n.Elements("Node").Remove();
            n.Add(new XElement(project.Components[(string)n.Attribute("component")!].Root!.Element("Node")!));
        }
        return [.. changed.Select(n => n.Document!).Distinct()];
    }
}
