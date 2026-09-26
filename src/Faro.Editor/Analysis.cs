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
        [.. Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories).SelectMany(f => Parse(File.ReadAllText(f)))];

    public static IEnumerable<RegistryMember> Parse(string code) =>
        from cls in CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
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
        select entry;

    /// <summary>
    /// Renames between the last-saved and the new content of one file (spec §6, run on save only).
    /// A rename = exactly one class, or exactly one same-kind member within a class, disappearing
    /// while one appears. Anything more ambiguous is left alone (the binding shows a red badge instead).
    /// Class renames come first so member targets can be rewritten after them.
    /// </summary>
    public static List<(string From, string To)> Renames(string savedCode, string newCode)
    {
        var before = Parse(savedCode).ToList();
        var after = Parse(newCode).ToList();
        var renames = new List<(string, string)>();

        var goneClasses = before.Select(ClassOf).Except(after.Select(ClassOf)).ToList();
        var newClasses = after.Select(ClassOf).Except(before.Select(ClassOf)).ToList();
        if (goneClasses.Count == 1 && newClasses.Count == 1)
        {
            renames.Add((goneClasses[0], newClasses[0]));
            before = [.. before.Select(m => ClassOf(m) == goneClasses[0] ? m with { Target = newClasses[0] + m.Target[goneClasses[0].Length..] } : m)];
        }

        foreach (var group in before.ExceptBy(after.Select(m => m.Target), m => m.Target).GroupBy(m => (ClassOf(m), m.IsMethod)))
        {
            var added = after.ExceptBy(before.Select(m => m.Target), m => m.Target).Where(m => (ClassOf(m), m.IsMethod) == group.Key).ToList();
            if (group.Count() == 1 && added.Count == 1) renames.Add((group.First().Target, added[0].Target));
        }
        return renames;
    }

    /// <summary>Applies a rename to a bind target ("Ns.Old" also rewrites "Ns.Old.Member").</summary>
    public static string Rename(string target, string from, string to) =>
        target == from || target.StartsWith(from + ".") ? to + target[from.Length..] : target;

    /// <summary>Rewrites bind targets for the given renames and returns the binding files to save.</summary>
    public static List<XDocument> FollowRenames(FaroProject project, List<(string From, string To)> renames)
    {
        var changed = new List<XDocument>();
        foreach (var bind in project.Binds)
        {
            var target = (string?)bind.Attribute("target") ?? "";
            var renamed = renames.Aggregate(target, (t, r) => Rename(t, r.From, r.To));
            if (renamed == target) continue;
            bind.SetAttributeValue("target", renamed);
            changed.Add(bind.Document!);
        }
        return [.. changed.Distinct()];
    }

    static string ClassOf(RegistryMember m) => m.Target[..m.Target.LastIndexOf('.')];

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

/// <summary>Vibe coding (spec §8): prompt, parsing of the generated files, and the checks before approval.</summary>
public static partial class VibeCoding
{
    public sealed record GeneratedFile(string Path, string Code);

    /// <summary>Instructions + current Source/ so the model can add to existing classes (spec §8).</summary>
    // ponytail: sends all of Source/ each turn; select relevant files when projects outgrow the context window
    public static string SystemPrompt(FaroProject project, string root, Lifetime lifetime, bool persistent)
    {
        var attribute = lifetime == Lifetime.ScreenScoped && !persistent
            ? "Do not add a [FaroLifetime] attribute (the default lifetime is ScreenScoped)."
            : $"Put [FaroLifetime(Lifetime.{lifetime}{(persistent ? ", Persistent = true" : "")})] on every new class.";
        var sourceDir = Path.Combine(root, "Source");
        var files = Directory.Exists(sourceDir)
            ? string.Join("\n\n", Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories).Order()
                .Select(f => $"File: {Path.GetRelativePath(root, f).Replace('\\', '/')}\n```csharp\n{File.ReadAllText(f)}\n```"))
            : "(none yet)";
        return $$"""
            You write C# for a Faro project: an Avalonia app whose UI (XML screens) is bound at runtime to public members
            of classes in Source/, addressed by the string "Namespace.Class.Member".

            Rules:
            - Event bindings (e.g. OnClick) call public parameterless methods. Property bindings use public properties.
            - Classes are public, top-level, and inherit Faro.Runtime.FaroObject (`using Faro.Runtime;`). Property setters raise
              change notifications: `public string Name { get; set => Set(ref field, value); }`. When a computed property depends on
              others, keep it in a field and update it with Set(ref ..., ..., nameof(Computed)) from those setters.
            - {{attribute}}
            - Screens: {{string.Join(", ", project.Screens.Keys.Order())}}. Navigate with Faro.Runtime.FaroApp.Navigate("ScreenId").
            - When changing an existing class, keep every existing member unless asked to remove it.

            Output format: for every file you create or change, write a line `File: Source/<Folder>/<Name>.cs` followed by a
            ```csharp block with the COMPLETE new content of that file. Only files under Source/. Keep explanations short.

            Current Source/ files:

            {{files}}
            """;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[ \t*`]*File:[ \t*`]*(?<path>[^\s`*]+)[ \t*`]*\r?\n```[A-Za-z#]*\r?\n(?<code>.*?)\r?\n```", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex FileBlock();

    public static List<GeneratedFile> ParseFiles(string response) =>
        [.. FileBlock().Matches(response).Select(m => new GeneratedFile(m.Groups["path"].Value, m.Groups["code"].Value + "\n"))];

    /// <summary>Model output is untrusted: only .cs files inside Source/ may be written. Returns the full path or null.</summary>
    public static string? ResolvePath(string root, string relative)
    {
        var source = Path.GetFullPath(Path.Combine(root, "Source")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return !Path.IsPathRooted(relative) && full.StartsWith(source) && full.EndsWith(".cs") ? full : null;
    }

    /// <summary>Roslyn syntax errors; approval is blocked while there are any (spec §11.5).</summary>
    public static List<string> SyntaxErrors(string code) =>
        [.. CSharpSyntaxTree.ParseText(code).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")];

    /// <summary>Public top-level class names declared in a file (for the per-class edit lock, spec §8.5).</summary>
    public static IEnumerable<string> ClassNames(string code) =>
        CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => c.Identifier.Text);

    /// <summary>Line diff: ' ' kept, '-' removed, '+' added.</summary>
    // ponytail: O(n·m) LCS table; switch to Myers if generated files get to thousands of lines
    public static List<(char Op, string Line)> Diff(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var diff = new List<(char, string)>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
            if (x < a.Length && y < b.Length && a[x] == b[y]) { diff.Add((' ', a[x++])); y++; }
            else if (x < a.Length && (y == b.Length || lcs[x + 1, y] >= lcs[x, y + 1])) diff.Add(('-', a[x++])); // removals first
            else diff.Add(('+', b[y++]));
        return diff;
    }
}

/// <summary>
/// UI graph edit history (spec §10). Faro-made changes to UI/ and Bindings/ are committed here as one
/// step each, with every touched file's content before and after. Undo/redo refuses to run when a file
/// was changed outside Faro since, instead of overwriting that change.
/// </summary>
public static class UiHistory
{
    sealed record Step(string Label, Dictionary<string, string?> Before, Dictionary<string, string?> After);

    static readonly List<Step> undo = [], redo = [];
    public static event Action? Changed;

    public static string? UndoLabel => undo.LastOrDefault()?.Label;
    public static string? RedoLabel => redo.LastOrDefault()?.Label;

    /// <summary>Saves the documents as one undoable step (no step if nothing changed on disk).</summary>
    public static void Commit(string label, IEnumerable<XDocument> docs)
    {
        var paths = docs.ToDictionary(d => new Uri(d.BaseUri).LocalPath);
        var before = paths.Keys.ToDictionary(p => p, Read);
        foreach (var doc in paths.Values) FaroProject.Save(doc);
        var after = paths.Keys.ToDictionary(p => p, Read);
        if (paths.Keys.All(p => before[p] == after[p])) return;
        undo.Add(new(label, before, after));
        redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>Returns an error message when the step can't be applied, else null.</summary>
    public static string? Undo() => Move(undo, redo, s => (s.After, s.Before));
    public static string? Redo() => Move(redo, undo, s => (s.Before, s.After));

    public static void Clear()
    {
        undo.Clear();
        redo.Clear();
        Changed?.Invoke();
    }

    static string? Move(List<Step> from, List<Step> to, Func<Step, (Dictionary<string, string?> Expected, Dictionary<string, string?> Target)> direction)
    {
        if (from.Count == 0) return null;
        var step = from[^1];
        var (expected, target) = direction(step);
        if (expected.FirstOrDefault(p => Read(p.Key) != p.Value) is { Key: not null } changed)
        {
            from.Clear(); // history no longer matches the files
            Changed?.Invoke();
            return $"{Path.GetFileName(changed.Key)} was changed outside Faro, so \"{step.Label}\" can't be undone or redone.";
        }
        foreach (var (path, text) in target)
            if (text is null) File.Delete(path); else File.WriteAllText(path, text);
        from.RemoveAt(from.Count - 1);
        to.Add(step);
        Changed?.Invoke();
        return null;
    }

    static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
