using System.Text.Json;
using System.Text.Json.Nodes;

namespace Faro.Editor;

/// <summary>
/// Creating and initializing Faro projects (welcome screen). A Faro project is a folder with faro.json
/// (name and language: fixed at creation, spec §2), the four folders (spec §4), a .csproj, and the
/// Faro.Runtime package vendored in .faro/packages so the project builds wherever the folder goes.
/// </summary>
public static partial class ProjectSetup
{
    public const string ProjectFile = "faro.json";
    public static readonly string[] Templates = ["Empty", "Sample"];
    public static readonly string[] Languages = ["CSharp", JavaProject.Language];

    /// <summary>Documents/Faro, where new projects go by default.</summary>
    // MyDocuments is empty on Linux without an XDG documents folder; fall back to ~/Documents.
    public static string DefaultLocation => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is { Length: > 0 } documents
            ? documents
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
        "Faro");

    /// <summary>The editor ships the runtime package and the templates next to itself.</summary>
    static string AppDir => AppContext.BaseDirectory;

    public static bool IsFaroProject(string dir) => File.Exists(Path.Combine(dir, ProjectFile));

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial System.Text.RegularExpressions.Regex NamePattern();

    /// <summary>Creates &lt;location&gt;/&lt;name&gt; from a template and returns its path.</summary>
    public static string Create(string location, string name, string template, string? appDir = null, string language = "CSharp")
    {
        var dir = Target(location, name);
        Directory.CreateDirectory(dir);
        var startScreen = "MainScreen";
        var sample = Path.Combine(appDir ?? AppDir, "templates", language == JavaProject.Language ? "SampleJava" : "Sample");
        if (template == "Sample")
            foreach (var file in Directory.EnumerateFiles(sample, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dir, Path.GetRelativePath(sample, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        else
            foreach (var (path, text) in ProjectFiles.NewScreen(Faro.Runtime.FaroProject.Load(dir), startScreen))
                WriteNew(path, text!);
        Initialize(dir, name, startScreen, appDir, language);
        return dir;
    }

    /// <summary>&lt;location&gt;/&lt;name&gt; for a new or saved-as project, validated.</summary>
    static string Target(string location, string name)
    {
        if (!NamePattern().IsMatch(name)) throw new ArgumentException($"'{name}' can't be a project name: use letters, digits and '_', starting with a letter.");
        if (!Path.IsPathRooted(location)) throw new ArgumentException("Choose a full folder path for the location.");
        var dir = Path.GetFullPath(Path.Combine(location, name));
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new ArgumentException($"{dir} already exists and isn't empty.");
        return dir;
    }

    // Untitled projects: a new project starts in UntitledRoot and gets its name and place on the first Save / Save As.

    public static string UntitledRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Faro", "Untitled");

    public static bool IsUntitled(string dir) =>
        Path.GetFullPath(dir).StartsWith(Path.GetFullPath(UntitledRoot) + Path.DirectorySeparatorChar);

    public static string CreateUntitled(string template, string? appDir = null, string language = "CSharp")
    {
        Directory.CreateDirectory(UntitledRoot);
        var n = 1;
        while (Directory.Exists(Path.Combine(UntitledRoot, $"Untitled{n}"))) n++;
        return Create(UntitledRoot, $"Untitled{n}", template, appDir, language);
    }

    /// <summary>
    /// Save As: copies the project (without the build outputs bin/, obj/, target/) to &lt;location&gt;/&lt;name&gt;, renaming it in faro.json and
    /// its generated .csproj. An untitled original is discarded. Returns the new folder, which the editor then opens.
    /// </summary>
    public static string SaveAs(string dir, string location, string name)
    {
        dir = Path.GetFullPath(dir);
        var target = Target(location, name);
        if (target.StartsWith(dir + Path.DirectorySeparatorChar)) throw new ArgumentException("Choose a location outside the project folder.");
        bool Copied(string path) => Path.GetRelativePath(dir, path).Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj" or "target");
        Directory.CreateDirectory(target);
        foreach (var folder in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).Where(Copied)) // empty ones too
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(dir, folder)));
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(Copied))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(dir, file)));
        var meta = Path.Combine(target, ProjectFile);
        if (File.Exists(meta) && JsonNode.Parse(File.ReadAllText(meta)) is JsonObject json)
        {
            var oldCsproj = Path.Combine(target, (string?)json["name"] + ".csproj");
            if (File.Exists(oldCsproj)) File.Move(oldCsproj, Path.Combine(target, name + ".csproj")); // a folder's own .csproj keeps its name
            var pom = Path.Combine(target, "pom.xml"); // Java: the app's artifact takes the new name too
            if (File.Exists(pom)) File.WriteAllText(pom, File.ReadAllText(pom).Replace($"<artifactId>{(string?)json["name"]}</artifactId>", $"<artifactId>{name}</artifactId>"));
            json["name"] = name;
            File.WriteAllText(meta, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        if (IsTrusted(dir)) Trust(target); // the copy holds the same code
        if (IsUntitled(dir)) Discard(dir);
        return target;
    }

    /// <summary>Untitled projects left behind (e.g. Faro closed abruptly): offered on the welcome screen to reopen or discard.</summary>
    public static List<string> LeftoverUntitled() =>
        !Directory.Exists(UntitledRoot) ? [] :
        [.. Directory.EnumerateDirectories(UntitledRoot).Select(Path.GetFullPath).Where(d => IsFaroProject(d) && !FaroSettings.Current.PendingDeletes.Contains(d)).Order()];

    /// <summary>Queues an untitled project for deletion; it happens on a later start, when no process holds its files.</summary>
    public static void Discard(string dir)
    {
        if (!IsUntitled(dir)) return; // never user folders
        FaroSettings.Current.PendingDeletes = [.. FaroSettings.Current.PendingDeletes.Append(Path.GetFullPath(dir)).Distinct()];
        FaroSettings.Current.Save();
    }

    /// <summary>Deletes discarded untitled projects (except <paramref name="open"/>); ones still in use are retried next time.</summary>
    public static void DeletePending(string? open = null)
    {
        var settings = FaroSettings.Current;
        settings.PendingDeletes = [.. settings.PendingDeletes.Where(dir =>
        {
            if (!IsUntitled(dir) || !Directory.Exists(dir)) return false;
            if (open is not null && Path.GetFullPath(open) == dir) return true;
            try { Directory.Delete(dir, recursive: true); return false; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
        })];
        settings.Save();
    }

    /// <summary>faro.json with the new "design" (left out when it's the Fluent default), for one UI history step.</summary>
    public static Dictionary<string, string?> DesignChange(string dir, Faro.Runtime.AppDesign design, IReadOnlyDictionary<string, string> tokens)
    {
        var path = Path.Combine(dir, ProjectFile);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json.Remove("design");
        if (design != new Faro.Runtime.AppDesign())
            json["design"] = new JsonObject { ["language"] = design.Language, ["seedColor"] = design.SeedColor, ["theme"] = design.Theme };
        json.Remove("tokens");
        if (tokens.Count > 0) // numbers stay numbers; colors and font names are strings
            json["tokens"] = new JsonObject(tokens.Select(t => KeyValuePair.Create(t.Key, (JsonNode?)(double.TryParse(t.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : JsonValue.Create(t.Value)))));
        return new() { [path] = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n" };
    }

    /// <summary>
    /// Makes a folder a Faro project: adds only what is missing and never overwrites existing files.
    /// A folder that already has a .csproj keeps it (it then needs a Faro.Runtime reference of its own).
    /// </summary>
    public static void Initialize(string dir, string? name = null, string startScreen = "MainScreen", string? appDir = null, string language = "CSharp")
    {
        name ??= new string(Path.GetFileName(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()) is { Length: > 0 } n && !char.IsDigit(n[0]) ? n : "FaroApp";
        foreach (var folder in new[] { "UI", "Source", "Bindings", "Assets" }) Directory.CreateDirectory(Path.Combine(dir, folder));

        var (package, version) = BundledRuntime(appDir);
        WriteNew(Path.Combine(dir, ProjectFile), new JsonObject { ["name"] = name, ["language"] = language, ["runtime"] = version.ToString(), ["startScreen"] = startScreen }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        if (language == JavaProject.Language)
        {
            JavaProject.Initialize(dir, name, version, appDir ?? AppDir);
            return;
        }
        Vendor(dir, package);
        WriteNew(Path.Combine(dir, "nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="faro" value=".faro/packages" />
              </packageSources>
            </configuration>

            """);
        if (!Directory.EnumerateFiles(dir, "*.csproj").Any())
            WriteNew(Path.Combine(dir, name + ".csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <!-- Generated by Faro. Language: C# (fixed for this project). -->
                  <PropertyGroup>
                    <OutputType>WinExe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Faro.Runtime" Version="{version}" />
                    <Content Include="faro.json;UI/**;Bindings/**;Assets/**" CopyToOutputDirectory="PreserveNewest" />
                    <!-- dotnet watch restarts the app when the UI graph or bindings change (spec §9). -->
                    <Watch Include="UI/**;Bindings/**" />
                  </ItemGroup>
                </Project>

                """);
        WriteNew(Path.Combine(dir, "Program.cs"), "Faro.Runtime.FaroApp.Run(args, typeof(Program).Assembly); // start screen: faro.json\n");
        WriteNew(Path.Combine(dir, ".gitignore"), "bin/\nobj/\ndist/\n.faro/android/\n");
    }

    /// <summary>
    /// Sort key of a Faro / runtime version: "0.2.4", or a pre-release "0.2.4-dev3" (work in progress), "0.2.4-canary.12",
    /// "0.2.4-beta.2" (release channels). Pre-releases sort before their release: dev &lt; canary &lt; beta &lt; release,
    /// then by number (0.1.9 &lt; 0.1.10). Null when the text isn't such a version.
    /// </summary>
    public static (Version Release, int Stage, int Number)? VersionKey(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+\.\d+\.\d+)(?:-(?:dev(\d+)|(canary|beta)\.(\d+)))?$");
        if (!match.Success) return null;
        var release = Version.Parse(match.Groups[1].Value);
        return match.Groups[2].Success ? (release, 0, int.Parse(match.Groups[2].Value))
            : match.Groups[3].Success ? (release, match.Groups[3].Value == "canary" ? 1 : 2, int.Parse(match.Groups[4].Value))
            : (release, 3, 0);
    }

    /// <summary>The newest Faro.Runtime package shipped with the editor.</summary>
    public static (string Package, string Version) BundledRuntime(string? appDir = null)
    {
        var packages = Path.Combine(appDir ?? AppDir, "runtime");
        return Directory.EnumerateFiles(packages, "Faro.Runtime.*.nupkg")
            .Select(p => (Package: p, Version: Path.GetFileNameWithoutExtension(p)["Faro.Runtime.".Length..]))
            .Where(p => VersionKey(p.Version) is not null).MaxBy(p => VersionKey(p.Version)) is { Package: not null } latest
            ? latest
            : throw new InvalidOperationException($"The Faro.Runtime package is missing from {packages}.");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(<PackageReference\s+Include=""Faro\.Runtime""\s+Version="")([^""]+)("")")]
    private static partial System.Text.RegularExpressions.Regex RuntimeReference();

    /// <summary>The Faro.Runtime version the project's .csproj references; null if it doesn't use the package (e.g. the in-repo sample).</summary>
    public static string? ProjectRuntime(string dir) => JavaProject.Is(dir) ? JavaProject.ProjectRuntime(dir) :
        Directory.EnumerateFiles(dir, "*.csproj").Select(f => RuntimeReference().Match(File.ReadAllText(f)))
            .FirstOrDefault(m => m.Success) is { } m && VersionKey(m.Groups[2].Value) is not null ? m.Groups[2].Value : null;

    /// <summary>Runtime update check on open: the editor ships a newer runtime than the project uses.</summary>
    public static bool RuntimeUpdateAvailable(string dir, string? appDir = null) =>
        ProjectRuntime(dir) is { } used && VersionKey(used)!.Value.CompareTo(VersionKey(BundledRuntime(appDir).Version)!.Value) < 0;

    /// <summary>Moves the project to the bundled runtime: vendors the package, bumps the .csproj reference and faro.json.</summary>
    public static void UpdateRuntime(string dir, string? appDir = null)
    {
        var (package, version) = BundledRuntime(appDir);
        if (JavaProject.Is(dir)) JavaProject.Vendor(dir, appDir ?? AppDir); // Java: the runtime's sources
        else
        {
            foreach (var old in Directory.EnumerateFiles(Path.Combine(dir, ".faro", "packages"), "Faro.Runtime.*.nupkg").Where(p => p != Path.Combine(dir, ".faro", "packages", Path.GetFileName(package))))
                File.Delete(old);
            Vendor(dir, package);
            foreach (var csproj in Directory.EnumerateFiles(dir, "*.csproj"))
                File.WriteAllText(csproj, RuntimeReference().Replace(File.ReadAllText(csproj), $"${{1}}{version}${{3}}"));
        }
        var json = Path.Combine(dir, ProjectFile);
        if (File.Exists(json) && JsonNode.Parse(File.ReadAllText(json)) is JsonObject meta)
        {
            meta["runtime"] = version.ToString();
            File.WriteAllText(json, meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
    }

    static void Vendor(string dir, string package)
    {
        var vendored = Path.Combine(dir, ".faro", "packages", Path.GetFileName(package));
        Directory.CreateDirectory(Path.GetDirectoryName(vendored)!);
        if (!File.Exists(vendored)) File.Copy(package, vendored);
    }

    internal static void WriteNew(string path, string text)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>
    /// Workspace trust: opening a project restores it (MSBuild), starts the language server and loads its build for
    /// Script previews, all of which run the project's code. Untitled projects are Faro's own and always trusted.
    /// </summary>
    public static bool IsTrusted(string dir) => IsUntitled(dir) || FaroSettings.Current.TrustedProjects.Contains(Path.GetFullPath(dir));

    public static void Trust(string dir)
    {
        if (IsTrusted(dir)) return;
        FaroSettings.Current.TrustedProjects = [.. FaroSettings.Current.TrustedProjects.Append(Path.GetFullPath(dir))];
        FaroSettings.Current.Save();
    }

    /// <summary>Most recent first, without duplicates, at most 10 (kept in the app settings).</summary>
    public static void Remember(string dir)
    {
        if (IsUntitled(dir)) return;
        var settings = FaroSettings.Current;
        settings.RecentProjects = [.. new[] { Path.GetFullPath(dir) }.Concat(settings.RecentProjects).Distinct().Take(10)];
        settings.Save();
    }
}
