using System.Xml.Linq;
using Faro.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Faro.Editor;

/// <summary>A bindable public member found in Source/. <c>Target</c> is the "Ns.Class.Member" string used by &lt;Bind&gt;.</summary>
public sealed record RegistryMember(string Target, bool IsMethod, string Signature);

/// <summary>A broken binding on <paramref name="Screen"/>: a screen or component id (node ids are unique per graph only).</summary>
/// <param name="Fix">The &lt;Bind&gt; attribute a suggestion replaces ("target", "event", "prop"); empty when suggestions are only hints.</param>
public sealed record BindingIssue(string NodeId, string Target, string Message, IReadOnlyList<string> Suggestions, string Screen = "", string Fix = "");

public static class Registry
{
    /// <summary>
    /// Extracts public members of public top-level classes with Roslyn syntax trees only,
    /// so it works on code that has never been built (spec §3).
    /// </summary>
    /// <remarks>Each file is parsed once per change (by its write time): a UI edit reloads the project without re-parsing Source/.</remarks>
    public static List<RegistryMember> Scan(string sourceDir) => [.. Files(sourceDir).SelectMany(f => f.Members)];

    /// <summary>Classes deriving from FaroScript (for Script nodes), by the same syntax-only scan.</summary>
    public static List<string> ScriptClasses(string sourceDir) => [.. Files(sourceDir).SelectMany(f => f.Scripts)];

    static readonly Dictionary<string, (DateTime Written, List<RegistryMember> Members, List<string> Scripts)> parsed = [];

    static IEnumerable<(List<RegistryMember> Members, List<string> Scripts)> Files(string sourceDir)
    {
        if (!Directory.Exists(sourceDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs") || f.EndsWith(".java")))
        {
            var written = File.GetLastWriteTimeUtc(file);
            lock (parsed)
            {
                if (!parsed.TryGetValue(file, out var entry) || entry.Written != written)
                {
                    var code = File.ReadAllText(file);
                    parsed[file] = entry = file.EndsWith(".java")
                        ? (written, JavaProject.Parse(code).ToList(), JavaProject.ScriptClasses(code).ToList())
                        : (written, Parse(code).ToList(), CSharpScripts(code).ToList());
                }
                yield return (entry.Members, entry.Scripts);
            }
        }
    }

    static IEnumerable<string> CSharpScripts(string code) =>
        from cls in CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
        where cls.Parent is not TypeDeclarationSyntax && IsPublic(cls.Modifiers)
            && cls.BaseList?.Types.Any(t => t.Type.ToString().Split('.')[^1] == "FaroScript") == true
        let ns = string.Join('.', cls.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()))
        select ns.Length > 0 ? $"{ns}.{cls.Identifier}" : cls.Identifier.Text;

    public static IEnumerable<RegistryMember> Parse(string code) =>
        from cls in CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
        where cls.Parent is not TypeDeclarationSyntax && IsPublic(cls.Modifiers)
        let ns = string.Join('.', cls.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()))
        let fullName = ns.Length > 0 ? $"{ns}.{cls.Identifier}" : cls.Identifier.Text
        from member in cls.Members
        where IsPublic(member.Modifiers) && !member.Modifiers.Any(SyntaxKind.OverrideKeyword) // e.g. a script's Build(): not a binding target
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

/// <summary>The app's screen flow (Figma's prototype view): who navigates where, from Navigate bindings.</summary>
public static class ScreenFlow
{
    /// <summary>Screen → screen links: a screen's own Navigate binds, and those of the components it holds (Bindings/&lt;Comp&gt;.xml).</summary>
    public static List<(string From, string To)> Edges(FaroProject project) =>
        project.Screens.SelectMany(screen =>
            project.BindsFor(screen.Key)
                .Concat(screen.Value.Root?.Element("Node") is { } root ? UiBuilder.InstancePaths(root).SelectMany(p => project.BindsFor(p.Component)) : [])
                .Select(b => (string?)b.Attribute("target") ?? "")
                .Where(t => t.StartsWith("Navigate:"))
                .Select(t => (From: screen.Key, To: FaroApp.NavigateScreenId(t, project.Screens.Keys))))
            .Where(e => project.Screens.ContainsKey(e.To))
            .Distinct().ToList();

    /// <summary>Columns by steps from the start screen (breadth first); screens nothing reaches go in a last column.</summary>
    public static List<List<string>> Columns(FaroProject project, List<(string From, string To)> edges)
    {
        var start = project.StartScreen is { } s && project.Screens.ContainsKey(s) ? s : project.Screens.Keys.FirstOrDefault();
        var columns = new List<List<string>>();
        var seen = new HashSet<string>();
        for (var layer = start is null ? [] : new List<string> { start }; layer.Count > 0; layer = [.. layer.SelectMany(f => edges.Where(e => e.From == f).Select(e => e.To)).Distinct().Where(seen.Add)])
        {
            if (columns.Count == 0) seen.Add(start!);
            columns.Add(layer);
        }
        if (project.Screens.Keys.Where(k => !seen.Contains(k)).Order().ToList() is { Count: > 0 } rest) columns.Add(rest);
        return columns;
    }
}

public static class BindingCheck
{
    /// <summary>Finds broken bindings (red badges, spec §6) with up to 3 near-name suggestions each.</summary>
    /// <param name="scripts">FaroScript classes in Source/: Script nodes naming another class are reported (null: not checked).</param>
    public static List<BindingIssue> Check(FaroProject project, List<RegistryMember> registry, IReadOnlyCollection<string>? scripts = null)
    {
        var issues = new List<BindingIssue>();
        var members = registry.Select(m => (m.Target, m.IsMethod)).ToHashSet();
        if (scripts is not null)
            foreach (var (graphId, graph) in project.Screens.Concat(project.Components))
                foreach (var n in NodesOf(graph).Where(n => (string?)n.Attribute("type") == "Control.Script" && !scripts.Contains((string?)n.Attribute("class") ?? "")))
                    issues.Add(new((string)n.Attribute("id")!, "", ((string?)n.Attribute("class") ?? "") is { Length: > 0 } cls
                        ? L.F("Script class '{0}' (deriving from FaroScript) not found in Source/.", cls) : L.T("No script class chosen yet."), Nearest((string?)n.Attribute("class") ?? "", scripts), graphId));
        foreach (var (screenId, screen) in project.Screens)
            foreach (var n in NodesOf(screen).Where(n => (string?)n.Attribute("type") == "Instance" && !project.Components.ContainsKey(ComponentSync.MasterId(n))))
                issues.Add(new((string)n.Attribute("id")!, "", L.F("Component '{0}' does not exist.", ComponentSync.MasterId(n)), Nearest(ComponentSync.MasterId(n), project.Components.Keys), screenId));

        if (project.StartScreen is { } start && !project.Screens.ContainsKey(start))
            issues.Add(new("", "", L.F("Start screen '{0}' (faro.json) does not exist.", start), Nearest(start, project.Screens.Keys), ""));
        foreach (var file in project.BindingFiles)
        {
            var screenId = FaroProject.ScreenOf(file);
            if (project.Graph(screenId) is not { } screen) // a screen, or a component master (component-level bindings)
            {
                issues.Add(new("", "", L.F("Bindings/{0}.xml doesn't belong to any screen or component.", screenId), Nearest(screenId, project.Screens.Keys.Concat(project.Components.Keys)), screenId));
                continue;
            }
            var nodes = NodesOf(screen).Where(n => n.Attribute("id") is not null).DistinctBy(n => (string)n.Attribute("id")!).ToDictionary(n => (string)n.Attribute("id")!);
            foreach (var bind in file.Root!.Elements("Bind"))
            {
                var nodeId = (string?)bind.Attribute("nodeId") ?? "";
                var target = (string?)bind.Attribute("target") ?? "";
                if ((nodeId.Contains('/') ? CanvasEdit.FindPath(screen, nodeId) : nodes.GetValueOrDefault(nodeId)) is not { } node)
                    issues.Add(new(nodeId, target, L.F("Node '{0}' does not exist on screen '{1}'.", nodeId, screenId), [], screenId));
                else if (Unbindable(bind, node) is { } problem)
                    issues.Add(problem with { Target = target, Screen = screenId });
                else if (target.Length == 0)
                    issues.Add(new(nodeId, target, L.T("No target chosen yet."), [], screenId));
                else if (target.StartsWith("Navigate:"))
                {
                    var to = FaroApp.NavigateScreenId(target, project.Screens.Keys);
                    if (!project.Screens.ContainsKey(to))
                        issues.Add(new(nodeId, target, L.F("Screen '{0}' does not exist.", to), Nearest(to, project.Screens.Keys).Select(s => $"Navigate:Screen.{s}").ToList(), screenId, "target"));
                }
                else
                {
                    var isEvent = bind.Attribute("event") is not null;
                    if (!members.Contains((target, isEvent)))
                        issues.Add(new(nodeId, target, L.F(isEvent ? "Method '{0}' not found in Source/." : "Property '{0}' not found in Source/.", target),
                            Nearest(target, registry.Where(m => m.IsMethod == isEvent).Select(m => m.Target)), screenId, "target"));
                }
            }
        }
        return issues;
    }

    /// <summary>The bind's event/prop must be one of the framework-neutral names for the node's type (Faro.Runtime.Bindable).</summary>
    static BindingIssue? Unbindable(XElement bind, XElement node)
    {
        var type = Bindable.TypeOf(node);
        var entry = Bindable.For(type);
        var isEvent = bind.Attribute("event") is not null;
        var name = (string?)bind.Attribute("event") ?? (string?)bind.Attribute("prop") ?? "";
        IEnumerable<string>? names = isEvent ? entry?.Events.Keys : entry?.Props.Keys;
        if (!isEvent && (string?)node.Attribute("repeatable") == "true") names = (names ?? []).Append("Items"); // a list: one copy per item (FaroApp)
        return names?.Contains(name) == true ? null
            : new((string)node.Attribute("id")!, "", L.F(isEvent ? "{0} has no event '{1}'." : "{0} has no property '{1}'.", type, name), Nearest(name, names ?? Enumerable.Empty<string>()), Fix: isEvent ? "event" : "prop");
    }

    /// <summary>Nodes authored in one screen, excluding the master snapshots stored inside instances.</summary>
    public static IEnumerable<XElement> NodesOf(XDocument screen) =>
        screen.Descendants("Node").Where(n => !n.Ancestors("Node").Any(a => (string?)a.Attribute("type") == "Instance"));

    /// <summary>Nodes authored in screens, excluding the master snapshots stored inside instances.</summary>
    public static IEnumerable<XElement> ScreenNodes(FaroProject project) => project.Screens.Values.SelectMany(NodesOf);

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
    /// <summary>The master an instance shows: its component, or the variant it picked ("Comp.X@Outlined" for variant="Outlined").</summary>
    public static string MasterId(XElement instance) =>
        (string?)instance.Attribute("component") + ((string?)instance.Attribute("variant") is { Length: > 0 } variant ? "@" + variant : "");

    public static List<XElement> OutOfDate(FaroProject project) =>
        [.. from n in BindingCheck.ScreenNodes(project)
            where (string?)n.Attribute("type") == "Instance"
            let master = project.Components.GetValueOrDefault(MasterId(n))?.Root!.Element("Node")
            where master is not null && !XNode.DeepEquals(n.Element("Node"), master)
            select n];

    /// <summary>Replaces the snapshots of out-of-date instances and returns the documents to save.</summary>
    public static List<XDocument> Sync(FaroProject project)
    {
        var changed = OutOfDate(project);
        foreach (var n in changed)
        {
            n.Elements("Node").Remove();
            n.Add(new XElement(project.Components[MasterId(n)].Root!.Element("Node")!));
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
        var java = JavaProject.Is(root);
        var attribute = lifetime == Lifetime.ScreenScoped && !persistent
            ? java ? "Do not add a @FaroLifetime annotation (the default lifetime is SCREEN_SCOPED)." : "Do not add a [FaroLifetime] attribute (the default lifetime is ScreenScoped)."
            : java ? $"Put @FaroLifetime(value = Lifetime.{(lifetime == Lifetime.ScreenScoped ? "SCREEN_SCOPED" : lifetime.ToString().ToUpperInvariant())}{(persistent ? ", persistent = true" : "")}) (import faro.runtime.*) on every new class."
            : $"Put [FaroLifetime(Lifetime.{lifetime}{(persistent ? ", Persistent = true" : "")})] on every new class.";
        var sourceDir = Path.Combine(root, "Source");
        var files = Directory.Exists(sourceDir)
            ? string.Join("\n\n", Directory.EnumerateFiles(sourceDir, java ? "*.java" : "*.cs", SearchOption.AllDirectories).Order()
                .Select(f => $"File: {Path.GetRelativePath(root, f).Replace('\\', '/')}\n```{(java ? "java" : "csharp")}\n{File.ReadAllText(f)}\n```"))
            : "(none yet)";
        if (java) return JavaProject.PromptRules(attribute, project.Screens.Keys.Order()) + "\n\nCurrent Source/ files:\n\n" + files;
        return $$"""
            You write C# for a Faro project: an Avalonia app whose UI (XML screens) is bound at runtime to public members
            of classes in Source/, addressed by the string "Namespace.Class.Member".

            Rules:
            - Event bindings (e.g. Click) call public parameterless methods. Property bindings use public properties.
            - A list (repeatable instance) binds prop "Items" to a collection property, best an ObservableCollection<T> so adds and
              removes redraw it. Binds to nodes inside the list ("orderList/name") then target members of T for each row.
            - A Script node's class inherits Faro.Runtime.FaroScript and overrides `public override Control Build()`, returning the
              Avalonia control (look and behaviour) shown in the node's place (`using Avalonia.Controls;`).
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

    /// <summary>Model output is untrusted: only source files (.cs, or .java in a Java project) inside Source/ may be written. Returns the full path or null.</summary>
    public static string? ResolvePath(string root, string relative)
    {
        var source = Path.GetFullPath(Path.Combine(root, "Source")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return !Path.IsPathRooted(relative) && full.StartsWith(source) && full.EndsWith(JavaProject.Is(root) ? ".java" : ".cs") ? full : null;
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

    /// <summary>Every step, oldest first; the first <see cref="Applied"/> are done, the rest were undone.</summary>
    public static List<string> Labels => [.. undo.Select(s => s.Label), .. Enumerable.Reverse(redo).Select(s => s.Label)];
    public static int Applied => undo.Count;

    /// <summary>Undoes or redoes until <paramref name="applied"/> steps are done (the history list); the first error stops it.</summary>
    public static string? GoTo(int applied)
    {
        while (undo.Count > applied) if (Undo() is { } error) return error;
        while (undo.Count < applied && redo.Count > 0) if (Redo() is { } error) return error;
        return null;
    }

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

    /// <summary>Writes (or, with null, deletes) whole files as one undoable step — for creating, renaming and deleting UI files.</summary>
    public static void CommitFiles(string label, IReadOnlyDictionary<string, string?> changes)
    {
        var before = changes.Keys.ToDictionary(p => p, Read);
        foreach (var (path, text) in changes)
            if (text is null) File.Delete(path);
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        var after = changes.Keys.ToDictionary(p => p, Read);
        if (changes.Keys.All(p => before[p] == after[p])) return;
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
            return L.F("{0} was changed outside Faro, so \"{1}\" can't be undone or redone.", Path.GetFileName(changed.Key), L.Step(step.Label));
        }
        foreach (var (path, text) in target)
            if (text is null) File.Delete(path);
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        from.RemoveAt(from.Count - 1);
        to.Add(step);
        Changed?.Invoke();
        return null;
    }

    static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}

/// <summary>
/// Canvas editing operations on the UI graph (spec §5). They mutate the loaded documents and return
/// the documents to commit, so each operation becomes one UI history step (spec §10).
/// </summary>
public static class CanvasEdit
{
    public static readonly string[] AddableTypes =
        ["Container.Stack", "Container.Wrap", "Container.Grid", "Container.Overlay", "Control.Button", "Control.TextInput", "Control.Text", "Control.Image",
         "Control.CheckBox", "Control.Switch", "Control.Slider", "Control.Select", "Control.Progress", "Control.Divider", "Control.Icon", "Control.Spacer", "Control.Script"];

    public static bool IsContainer(XElement node) => ((string?)node.Attribute("type"))?.StartsWith("Container.") == true;

    /// <summary>
    /// A bind's node: a screen node ("price"), or a node inside an instance's synced snapshot by path
    /// ("orderList/price", nested "card/ok/root") — the same keys the runtime registers.
    /// </summary>
    public static XElement? FindPath(XDocument graph, string path)
    {
        XElement? node = null;
        foreach (var part in path.Split('/'))
        {
            var scope = node is null ? BindingCheck.NodesOf(graph) : InnerNodes(node);
            if ((node = scope.FirstOrDefault(n => (string?)n.Attribute("id") == part)) is null) return null;
        }
        return node;
    }

    /// <summary>The nodes of an instance's snapshot one level deep (a nested instance counts as one node).</summary>
    public static IEnumerable<XElement> InnerNodes(XElement instance) =>
        (string?)instance.Attribute("type") == "Instance" && instance.Element("Node") is { } root
            ? root.DescendantsAndSelf("Node").Where(n => n == root || !n.Ancestors("Node").TakeWhile(a => a != root).Any(a => (string?)a.Attribute("type") == "Instance"))
            : [];

    /// <summary>A node authored in the screen (not inside an instance's master snapshot).</summary>
    public static XElement? Find(XDocument screen, string id) =>
        screen.Descendants("Node").FirstOrDefault(n => (string?)n.Attribute("id") == id
            && !n.Ancestors("Node").Any(a => (string?)a.Attribute("type") == "Instance"));

    static string ScreenId(XDocument screen) => (string)screen.Root!.Attribute("id")!;

    static HashSet<string?> IdsOf(XDocument screen) => [.. BindingCheck.NodesOf(screen).Select(n => (string?)n.Attribute("id"))];

    /// <summary>A node id not used on this screen (ids are unique per screen, spec §5).</summary>
    public static string NewId(XDocument screen, string type)
    {
        var stem = type.Split('.')[^1].ToLowerInvariant() switch { "textinput" => "input", var s => s };
        var ids = IdsOf(screen);
        return Enumerable.Range(1, int.MaxValue).Select(i => $"{stem}{i}").First(id => !ids.Contains(id));
    }

    /// <summary>Prop value; on an instance this is its Override (falling back to the master snapshot).</summary>
    public static string? GetProp(XElement node, string name) =>
        (string?)node.Elements("Override").FirstOrDefault(o => (string?)o.Attribute("prop") == name)?.Attribute("value")
        ?? UiBuilder.Prop(node.Element("Node") is { } snapshot && (string?)node.Attribute("type") == "Instance" ? snapshot : node, name);

    /// <summary>Sets or (with null/empty) removes a Prop — as an Override on instances (spec §5).</summary>
    public static void SetProp(XElement node, string name, string? value)
    {
        var instance = (string?)node.Attribute("type") == "Instance";
        var (element, key) = instance ? ("Override", "prop") : ("Prop", "name");
        var existing = node.Elements(element).FirstOrDefault(e => (string?)e.Attribute(key) == name);
        if (string.IsNullOrEmpty(value)) { existing?.Remove(); return; }
        if (existing is not null) existing.SetAttributeValue("value", value);
        else
        {
            var prop = new XElement(element, new XAttribute(key, name), new XAttribute("value", value));
            // Props/Overrides go before child nodes, like the hand-written files.
            if (node.Element("Node") is { } firstChild) firstChild.AddBeforeSelf(prop); else node.Add(prop);
        }
    }

    /// <summary>Sets or (with null/empty) removes a layout attribute such as widthSizing or gap.</summary>
    public static void SetAttribute(XElement node, string name, string? value) =>
        node.SetAttributeValue(name, string.IsNullOrEmpty(value) ? null : value);

    /// <summary>Adds a node into the selected container, else after the selected node, else at the end of the root.</summary>
    public static XElement Add(FaroProject project, XDocument screen, string? selectedId, string type, string? component = null)
    {
        var id = NewId(screen, component is null ? type : component.Split('.')[^1]);
        var node = new XElement("Node", new XAttribute("id", id), new XAttribute("type", component is null ? type : "Instance"));
        if (component is not null)
        {
            node.SetAttributeValue("component", component);
            node.Add(new XElement(project.Components[component].Root!.Element("Node")!)); // initial snapshot of the master
        }
        else if (type.StartsWith("Container.")) { node.SetAttributeValue("gap", "8"); node.SetAttributeValue("padding", "8"); }
        else if (type == "Control.Script") node.SetAttributeValue("class", "");
        else
            foreach (var (name, value) in type switch
            {
                "Control.Button" => [("Text", "Button")],
                "Control.Text" => [("Text", "Text")],
                "Control.CheckBox" => [("Text", "Check")],
                "Control.Switch" => [("Text", "Switch")],
                "Control.Slider" => [("Minimum", "0"), ("Maximum", "100"), ("Value", "50")],
                "Control.Select" => [("Options", "Option 1, Option 2, Option 3"), ("Selected", "Option 1")],
                "Control.Progress" => [("Value", "50")],
                "Control.Icon" => [("Icon", "star")],
                _ => Array.Empty<(string, string)>(),
            })
                node.Add(new XElement("Prop", new XAttribute("name", name), new XAttribute("value", value)));

        var selected = selectedId is null ? null : Find(screen, selectedId);
        if (selected is not null && IsContainer(selected)) selected.Add(node);
        else if (selected?.Parent is XElement { Name.LocalName: "Node" }) selected.AddAfterSelf(node);
        else screen.Root!.Element("Node")!.Add(node);
        return node;
    }

    /// <summary>
    /// A spacing handle on the canvas (Figma): a container's "gap", or one side of its "padding" (0 top, 1 right, 2 bottom,
    /// 3 left), written back as one value when all four sides match, else "top right bottom left".
    /// </summary>
    public static bool SetSpacing(XDocument screen, string id, bool gap, int side, double value)
    {
        if (Find(screen, id) is not { } node || !IsContainer(node)) return false;
        var text = Math.Max(0, Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (gap) { SetAttribute(node, "gap", text); return true; }
        var t = UiBuilder.Sides((string?)node.Attribute("padding"));
        var sides = new[] { t.Top, t.Right, t.Bottom, t.Left };
        sides[side] = Math.Max(0, Math.Round(value));
        SetAttribute(node, "padding", sides.Distinct().Count() == 1 ? text : string.Join(" ", sides.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return true;
    }

    /// <summary>Distribute (Figma): nodes side by side in one Stack spread over it, the free space evenly between them.</summary>
    public static bool Distribute(XDocument screen, IReadOnlyCollection<string> ids)
    {
        var parents = ids.Select(id => Find(screen, id)?.Parent).Distinct().ToList();
        if (ids.Count < 2 || parents is not [{ Name.LocalName: "Node" } parent] || (string?)parent.Attribute("type") != "Container.Stack") return false;
        SetAttribute(parent, "justify", "SpaceBetween");
        return true;
    }

    /// <summary>
    /// Figma's align buttons for one node (<paramref name="horizontal"/> left/center/right, else top/middle/bottom), done the
    /// way its container lays out: across a Stack, the node's own alignSelf; along a Stack, Spacers beside it (right end = a
    /// Spacer before it, center = one on each side, start = none); in an Overlay, its anchor; in a Grid, alignSelf in its cell.
    /// False when nothing applies (a Wrap, the root).
    /// </summary>
    public static bool Align(XDocument screen, string id, bool horizontal, string where)
    {
        if (Find(screen, id) is not { } node || node.Parent is not { Name.LocalName: "Node" } parent) return false;
        var type = (string?)parent.Attribute("type");
        if (type == "Container.Overlay")
        {
            SetAttribute(node, horizontal ? "anchorX" : "anchorY", horizontal ? where switch { "Center" => "Center", "End" => "Right", _ => null } : where switch { "Center" => "Center", "End" => "Bottom", _ => null });
            return true;
        }
        if (type == "Container.Grid") { SetAttribute(node, "alignSelf", where == "Start" ? null : where); return true; }
        if (type != "Container.Stack") return false;
        if (horizontal != ((string?)parent.Attribute("direction") == "Horizontal")) // across the stack
        {
            SetAttribute(node, "alignSelf", where == ((string?)parent.Attribute("alignment") ?? "Start") ? null : where);
            return true;
        }
        static bool IsSpacer(XNode? n) => n is XElement { Name.LocalName: "Node" } e && (string?)e.Attribute("type") == "Control.Spacer";
        if (IsSpacer(node.PreviousNode)) node.PreviousNode!.Remove();
        if (IsSpacer(node.NextNode)) node.NextNode!.Remove();
        XElement Spacer() => new("Node", new XAttribute("id", NewId(screen, "Control.Spacer")), new XAttribute("type", "Control.Spacer"));
        if (where is "End" or "Center") node.AddBeforeSelf(Spacer());
        if (where == "Center") node.AddAfterSelf(Spacer());
        return true;
    }

    /// <summary>Wraps sibling nodes in a new container at the first one's place; their ids and binds stay. Null if they aren't siblings.</summary>
    public static XElement? Wrap(XDocument screen, IEnumerable<string> ids, string type)
    {
        var nodes = ids.Select(id => Find(screen, id)).OfType<XElement>().Where(n => n.Parent?.Name == "Node").OrderBy(n => n.ElementsBeforeSelf().Count()).ToList();
        if (nodes.Count == 0 || nodes.Select(n => n.Parent).Distinct().Count() != 1) return null;
        var container = new XElement("Node", new XAttribute("id", NewId(screen, type)), new XAttribute("type", type));
        if (type != "Container.Overlay") container.SetAttributeValue("gap", "8");
        nodes[0].AddBeforeSelf(container);
        foreach (var n in nodes) { n.Remove(); container.Add(n); }
        return container;
    }

    /// <summary>Copied nodes (deep) and the binds that point into them, pasteable on any screen.</summary>
    public sealed record Clip(List<XElement> Nodes, List<XElement> Binds);

    /// <summary>Nodes authored in a subtree: the node and its descendants, not the synced snapshots inside instances.</summary>
    static IEnumerable<XElement> Authored(XElement node) =>
        node.DescendantsAndSelf("Node").Where(d => !d.Ancestors("Node").TakeWhile(a => a != node.Parent).Any(a => a != d && (string?)a.Attribute("type") == "Instance"));

    public static Clip Copy(FaroProject project, XDocument screen, IEnumerable<string> ids)
    {
        var nodes = ids.Select(id => Find(screen, id)).OfType<XElement>().Where(n => n.Parent?.Name == "Node").ToList();
        nodes = [.. nodes.Where(n => !n.Ancestors().Any(nodes.Contains)).OrderBy(n => n.ElementsBeforeSelf().Count())]; // a selected child goes with its selected parent
        var copied = nodes.SelectMany(Authored).Select(n => (string?)n.Attribute("id")).ToHashSet();
        return new([.. nodes.Select(n => new XElement(n))],
            [.. project.BindsFor(ScreenId(screen)).Where(b => copied.Contains(((string?)b.Attribute("nodeId"))?.Split('/')[0])).Select(b => new XElement(b))]);
    }

    /// <summary>
    /// Pastes into the selected container (or after the selected node; <paramref name="after"/> forces that, for
    /// Duplicate). Ids that are taken on this screen get the next free number ("btn1" → "btn2"); copied binds follow.
    /// </summary>
    public static (List<XElement> Added, List<XDocument> Changed) Paste(FaroProject project, XDocument screen, string? selectedId, Clip clip, bool after = false)
    {
        var taken = IdsOf(screen);
        var renamed = new Dictionary<string, string>();
        string Unique(string id)
        {
            var stem = id.TrimEnd("0123456789".ToCharArray());
            var fresh = taken.Contains(id) ? Enumerable.Range(2, int.MaxValue - 2).Select(i => $"{stem}{i}").First(c => !taken.Contains(c)) : id;
            taken.Add(fresh);
            return fresh;
        }
        var selected = selectedId is null ? null : Find(screen, selectedId);
        var into = selected is not null && IsContainer(selected) && !after ? selected : null;
        var last = into is null && selected?.Parent is XElement { Name.LocalName: "Node" } ? selected : null;
        var added = new List<XElement>();
        foreach (var original in clip.Nodes)
        {
            var node = new XElement(original);
            foreach (var n in Authored(node).ToList())
                n.SetAttributeValue("id", renamed[(string)n.Attribute("id")!] = Unique((string)n.Attribute("id")!));
            if (into is not null) into.Add(node);
            else if (last is not null) { last.AddAfterSelf(node); last = node; }
            else screen.Root!.Element("Node")!.Add(node);
            added.Add(node);
        }
        List<XDocument> changed = [screen];
        if (clip.Binds.Count > 0)
        {
            var file = BindingsFileFor(project, ScreenId(screen));
            foreach (var bind in clip.Binds)
            {
                var path = ((string?)bind.Attribute("nodeId") ?? "").Split('/', 2);
                if (!renamed.TryGetValue(path[0], out var id)) continue;
                var copy = new XElement(bind);
                copy.SetAttributeValue("nodeId", path.Length > 1 ? $"{id}/{path[1]}" : id);
                file.Root!.Add(copy);
            }
            changed.Add(file);
        }
        return (added, changed);
    }

    /// <summary>Deletes nodes (never the screen root) and the bindings that pointed at them.</summary>
    public static List<XDocument> Delete(FaroProject project, XDocument screen, IEnumerable<string> ids)
    {
        var nodes = ids.Select(id => Find(screen, id)).OfType<XElement>().Where(n => n.Parent?.Name == "Node").ToList();
        var gone = nodes.SelectMany(n => n.DescendantsAndSelf("Node")).Select(n => (string?)n.Attribute("id")).ToHashSet();
        var binds = project.BindsFor(ScreenId(screen)).Where(b => gone.Contains(((string?)b.Attribute("nodeId"))?.Split('/')[0])).ToList(); // "orderList/price" goes with orderList
        var changed = binds.Select(b => b.Document!).Distinct().Append(screen).ToList();
        nodes.ForEach(n => n.Remove());
        binds.ForEach(b => b.Remove());
        return nodes.Count > 0 ? changed : [];
    }

    /// <summary>Moves a node one place up (-1) or down (+1) among its siblings. False when it can't move.</summary>
    public static bool Move(XDocument screen, string id, int delta)
    {
        if (Find(screen, id) is not { Parent: XElement { Name.LocalName: "Node" } } node) return false;
        var sibling = delta < 0 ? node.ElementsBeforeSelf("Node").LastOrDefault() : node.ElementsAfterSelf("Node").FirstOrDefault();
        if (sibling is null) return false;
        node.Remove();
        if (delta < 0) sibling.AddBeforeSelf(node); else sibling.AddAfterSelf(node);
        return true;
    }

    /// <summary>
    /// Drag and drop: moves a node into <paramref name="parentId"/> at child position <paramref name="index"/>
    /// (counted without the moved node). False when the drop is invalid (root, non-container, or into itself).
    /// </summary>
    public static bool MoveTo(XDocument screen, string id, string parentId, int index)
    {
        if (Find(screen, id) is not { Parent: XElement { Name.LocalName: "Node" } } node || Find(screen, parentId) is not { } parent
            || !IsContainer(parent) || parent.AncestorsAndSelf().Contains(node)) return false;
        var siblings = parent.Elements("Node").Where(n => n != node).ToList();
        node.Remove();
        index = Math.Clamp(index, 0, siblings.Count);
        if (index < siblings.Count) siblings[index].AddBeforeSelf(node); else parent.Add(node);
        return true;
    }

    /// <summary>Renames a node and follows its bindings (spec §6). Returns an error message, or null and the docs to commit.</summary>
    public static (string? Error, List<XDocument> Changed) Rename(FaroProject project, XDocument screen, string oldId, string newId)
    {
        newId = newId.Trim();
        if (newId.Length == 0 || newId.Contains('/') || newId.Any(char.IsWhiteSpace)) return ("An ID can't be empty or contain '/' or spaces.", []);
        if (newId == oldId) return (null, []);
        if (IdsOf(screen).Contains(newId)) return ($"'{newId}' is already used on this screen.", []);
        if (Find(screen, oldId) is not { } node) return ($"'{oldId}' not found.", []);
        node.SetAttributeValue("id", newId);
        var binds = project.BindsFor(ScreenId(screen)).Where(b => (string?)b.Attribute("nodeId") is { } n && (n == oldId || n.StartsWith(oldId + "/"))).ToList();
        binds.ForEach(b => b.SetAttributeValue("nodeId", newId + ((string)b.Attribute("nodeId")!)[oldId.Length..]));
        return (null, [screen, .. binds.Select(b => b.Document!).Distinct()]);
    }

    /// <summary>The screen's own Bindings/&lt;screenId&gt;.xml, created (empty) when missing.</summary>
    // ponytail: the created file stays behind (empty) if the adding step is undone; harmless
    public static XDocument BindingsFileFor(FaroProject project, string screenId)
    {
        screenId = screenId.Split('@')[0]; // a variant's binds are its component's
        if (project.BindingFiles.FirstOrDefault(d => FaroProject.ScreenOf(d) == screenId) is { } existing) return existing;
        var path = Path.Combine(project.Root, "Bindings", screenId + ".xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Bindings>\n</Bindings>\n");
        var doc = XDocument.Load(path, LoadOptions.SetBaseUri);
        project.BindingFiles.Add(doc);
        return doc;
    }
}

/// <summary>
/// Explorer operations on screen / component files (UI/*.xml). Each returns the file changes
/// (path → new content, null = delete) to commit as one UI history step, or throws with a user-facing message.
/// </summary>
public static partial class ProjectFiles
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    /// <summary>Images under Assets/ as the project-relative paths a Source prop holds ("Assets/logo.png").</summary>
    public static List<string> Images(string root) =>
        !Directory.Exists(Path.Combine(root, "Assets")) ? [] :
        [.. Directory.EnumerateFiles(Path.Combine(root, "Assets"), "*", SearchOption.AllDirectories)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order()];

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial System.Text.RegularExpressions.Regex IdPattern();

    /// <summary>User input becomes a file name: letters, digits, '_' and '.', starting with a letter or '_'.</summary>
    static void ValidateNewId(FaroProject project, string id)
    {
        if (!IdPattern().IsMatch(id)) throw new ArgumentException($"'{id}' is not a valid ID (letters, digits, '_' and '.', starting with a letter).");
        if (project.Screens.ContainsKey(id) || project.Components.ContainsKey(id) || File.Exists(Path.Combine(project.Root, "UI", id + ".xml")))
            throw new ArgumentException($"'{id}' is already used.");
    }

    public static Dictionary<string, string?> NewScreen(FaroProject project, string id)
    {
        ValidateNewId(project, id);
        return new() { [Path.Combine(project.Root, "UI", id + ".xml")] = Text(new XDocument(new XElement("UIGraph", new XAttribute("id", id), new XAttribute("version", "1"),
            new XElement("Node", new XAttribute("id", "root"), new XAttribute("type", "Container.Stack"), new XAttribute("direction", "Vertical"), new XAttribute("gap", "8"), new XAttribute("padding", "16"))))) };
    }

    public static Dictionary<string, string?> NewComponent(FaroProject project, string id)
    {
        ValidateNewId(project, id);
        return new() { [Path.Combine(project.Root, "UI", id + ".xml")] = Text(new XDocument(new XElement("ComponentDef", new XAttribute("id", id),
            new XElement("Node", new XAttribute("id", "root"), new XAttribute("type", "Container.Stack"), new XAttribute("gap", "8"))))) };
    }

    /// <summary>
    /// A variant of a component (Figma variants): a copy of its master saved as "Comp.X@Name" and edited like any master.
    /// Instances pick one with variant="Name" (their snapshot comes from it); all variants share Comp.X's bindings.
    /// </summary>
    public static Dictionary<string, string?> NewVariant(FaroProject project, string component, string name)
    {
        if (!IdPattern().IsMatch(name)) throw new ArgumentException($"'{name}' is not a valid variant name (letters, digits, '_' and '.', starting with a letter).");
        var id = $"{component}@{name}";
        if (project.Components.ContainsKey(id)) throw new ArgumentException($"'{name}' is already a variant of {component}.");
        var copy = new XDocument(project.Components[component]);
        copy.Root!.SetAttributeValue("id", id);
        return new() { [Path.Combine(project.Root, "UI", id + ".xml")] = Text(copy) };
    }

    /// <summary>The variant names of a component ("Outlined" for Comp.X@Outlined).</summary>
    public static List<string> Variants(FaroProject project, string component) =>
        [.. project.Components.Keys.Where(k => k.StartsWith(component + "@")).Select(k => k[(component.Length + 1)..]).Order()];

    /// <summary>Renames a screen (id and file) and follows Navigate targets and the start screen in faro.json.</summary>
    public static Dictionary<string, string?> RenameScreen(FaroProject project, string oldId, string newId)
    {
        ValidateNewId(project, newId);
        var changes = MoveDoc(project, project.Screens[oldId], newId);
        foreach (var file in project.BindingFiles)
        {
            var binds = file.Root!.Elements("Bind").Where(b => (string?)b.Attribute("target") is { } t && t.StartsWith("Navigate:")
                && FaroApp.NavigateScreenId(t, project.Screens.Keys) == oldId).ToList();
            binds.ForEach(b => b.SetAttributeValue("target", $"Navigate:Screen.{newId}"));
            if (binds.Count > 0) changes[PathOf(file)] = Text(file);
        }
        MoveBindings(project, oldId, newId, changes);
        var meta = Path.Combine(project.Root, "faro.json");
        if (project.StartScreen == oldId && System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(meta)) is System.Text.Json.Nodes.JsonObject json)
        {
            json["startScreen"] = newId;
            changes[meta] = json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
        }
        return changes;
    }

    /// <summary>Renames a component (id and file) and follows every instance that uses it.</summary>
    public static Dictionary<string, string?> RenameComponent(FaroProject project, string oldId, string newId)
    {
        ValidateNewId(project, newId);
        var changes = MoveDoc(project, project.Components[oldId], newId);
        foreach (var variant in Variants(project, oldId)) // its variants keep their names under the new id
            foreach (var (path, text) in MoveDoc(project, project.Components[$"{oldId}@{variant}"], $"{newId}@{variant}")) changes[path] = text;
        MoveBindings(project, oldId, newId, changes);
        foreach (var doc in project.Screens.Values.Concat(project.Components.Values).Where(d => d != project.Components[oldId]))
        {
            var instances = doc.Descendants("Node").Where(n => (string?)n.Attribute("component") == oldId).ToList();
            instances.ForEach(n => n.SetAttributeValue("component", newId));
            if (instances.Count > 0) changes[PathOf(doc)] = Text(doc);
        }
        return changes;
    }

    /// <summary>Deletes a screen or a component, with its bindings file.</summary>
    public static Dictionary<string, string?> Delete(FaroProject project, XDocument doc)
    {
        var changes = new Dictionary<string, string?> { [PathOf(doc)] = null };
        if (project.BindingFiles.FirstOrDefault(d => FaroProject.ScreenOf(d) == (string?)doc.Root!.Attribute("id")) is { } own)
            changes[PathOf(own)] = null;
        return changes;
    }

    /// <summary>A screen's or component's own bindings file moves with its id.</summary>
    static void MoveBindings(FaroProject project, string oldId, string newId, Dictionary<string, string?> changes)
    {
        if (project.BindingFiles.FirstOrDefault(d => FaroProject.ScreenOf(d) == oldId) is not { } own) return;
        changes[PathOf(own)] = null;
        changes[Path.Combine(project.Root, "Bindings", newId + ".xml")] = Text(own);
    }

    /// <summary>A new C# class file for Source/, in the namespace the project already uses (spec §6: FaroObject for change notification).</summary>
    public static string ClassFile(string sourceDir, string relativeFolder, string name)
    {
        if (!IdPattern().IsMatch(name) || name.Contains('.')) throw new ArgumentException($"'{name}' is not a valid class name.");
        var rootNs = Directory.Exists(sourceDir)
            ? Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f)).GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
                .Select(n => n.Name.ToString().Split('.')[0]).FirstOrDefault()
            : null;
        var ns = string.Join('.', new[] { rootNs ?? "App" }.Concat(relativeFolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)));
        return $"using Faro.Runtime;\n\nnamespace {ns};\n\npublic class {name} : FaroObject\n{{\n}}\n";
    }

    static Dictionary<string, string?> MoveDoc(FaroProject project, XDocument doc, string newId)
    {
        doc.Root!.SetAttributeValue("id", newId);
        return new() { [PathOf(doc)] = null, [Path.Combine(project.Root, "UI", newId + ".xml")] = Text(doc) };
    }

    static string PathOf(XDocument doc) => new Uri(doc.BaseUri).LocalPath;

    /// <summary>The same bytes XDocument.Save writes (UTF-8 with BOM and declaration).</summary>
    static string Text(XDocument doc)
    {
        using var stream = new MemoryStream();
        doc.Save(stream);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// Design-time mock rows for repeatable nodes (spec §10.5), stored under the node as
/// &lt;MockRow&gt;&lt;Set node="name" value="りんご" /&gt;&lt;/MockRow&gt;. The canvas shows one copy per row;
/// the runtime ignores them (wiring real data is deferred, spec §5). Values fill Text, or an image's Source.
/// </summary>
public static class MockData
{
    /// <summary>Nodes a row fills: those with a Text inside the repeatable node (an instance's synced snapshot).</summary>
    public static List<string> Fields(XElement node) =>
        [.. Inner(node).DescendantsAndSelf("Node").Where(n => Bindable.TypeOf(n) == "Control.Image" || Bindable.For(Bindable.TypeOf(n))?.Props.ContainsKey("Text") == true)
            .Select(n => (string)n.Attribute("id")!).Distinct()];

    /// <summary>What a row value fills: an image's Source (an Assets/ path), otherwise the Text.</summary>
    static string PropOf(XElement node) => Bindable.TypeOf(node) == "Control.Image" ? "Source" : "Text";

    public static List<List<string>> Rows(XElement node) =>
        [.. node.Elements("MockRow").Select(row => Fields(node).Select(f => (string?)row.Elements("Set").FirstOrDefault(s => (string?)s.Attribute("node") == f)?.Attribute("value") ?? "").ToList())];

    public static void SetRows(XElement node, IEnumerable<IReadOnlyList<string>> rows)
    {
        node.Elements("MockRow").Remove();
        var fields = Fields(node);
        foreach (var row in rows)
            node.Add(new XElement("MockRow", fields.Zip(row).Where(p => p.Second.Length > 0).Select(p => new XElement("Set", new XAttribute("node", p.First), new XAttribute("value", p.Second)))));
    }

    /// <summary>A copy of the tree with each repeatable node shown once per mock row (extra copies get ids "id~2", "id~3"…).</summary>
    public static XElement Expand(XElement root)
    {
        var copy = new XElement(root);
        foreach (var node in copy.DescendantsAndSelf("Node").Where(n => (string?)n.Attribute("repeatable") == "true" && n.Elements("MockRow").Any() && n.Parent is not null).ToList())
        {
            var rows = node.Elements("MockRow").ToList();
            var template = new XElement(node);
            var last = node;
            for (var i = 0; i < rows.Count; i++)
            {
                var shown = i == 0 ? node : new XElement(template);
                if (i > 0) { shown.SetAttributeValue("id", $"{(string?)node.Attribute("id")}~{i + 1}"); last.AddAfterSelf(shown); last = shown; }
                foreach (var set in rows[i].Elements("Set"))
                    if (Inner(shown).DescendantsAndSelf("Node").FirstOrDefault(n => (string?)n.Attribute("id") == (string?)set.Attribute("node")) is { } target)
                        CanvasEdit.SetProp(target == Inner(shown) && shown != target ? shown : target, PropOf(target), (string?)set.Attribute("value")); // an instance's root prop is an Override
            }
        }
        return copy;
    }

    static XElement Inner(XElement node) => (string?)node.Attribute("type") == "Instance" && node.Element("Node") is { } snapshot ? snapshot : node;
}
