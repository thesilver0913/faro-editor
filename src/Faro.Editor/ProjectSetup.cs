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
    public static string Create(string location, string name, string template, string? appDir = null)
    {
        if (!NamePattern().IsMatch(name)) throw new ArgumentException($"'{name}' can't be a project name: use letters, digits and '_', starting with a letter.");
        if (!Path.IsPathRooted(location)) throw new ArgumentException("Choose a full folder path for the location.");
        var dir = Path.Combine(location, name);
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new ArgumentException($"{dir} already exists and isn't empty.");
        Directory.CreateDirectory(dir);
        var startScreen = "MainScreen";
        if (template == "Sample")
            foreach (var file in Directory.EnumerateFiles(Path.Combine(appDir ?? AppDir, "templates", "Sample"), "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dir, Path.GetRelativePath(Path.Combine(appDir ?? AppDir, "templates", "Sample"), file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        else
            foreach (var (path, text) in ProjectFiles.NewScreen(Faro.Runtime.FaroProject.Load(dir), startScreen))
                WriteNew(path, text!);
        Initialize(dir, name, startScreen, appDir);
        return dir;
    }

    /// <summary>
    /// Makes a folder a Faro project: adds only what is missing and never overwrites existing files.
    /// A folder that already has a .csproj keeps it (it then needs a Faro.Runtime reference of its own).
    /// </summary>
    public static void Initialize(string dir, string? name = null, string startScreen = "MainScreen", string? appDir = null)
    {
        name ??= new string(Path.GetFileName(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()) is { Length: > 0 } n && !char.IsDigit(n[0]) ? n : "FaroApp";
        foreach (var folder in new[] { "UI", "Source", "Bindings", "Assets" }) Directory.CreateDirectory(Path.Combine(dir, folder));

        var packages = Path.Combine(appDir ?? AppDir, "runtime");
        var package = Directory.EnumerateFiles(packages, "Faro.Runtime.*.nupkg").Order().LastOrDefault()
            ?? throw new InvalidOperationException($"The Faro.Runtime package is missing from {packages}.");
        var version = Path.GetFileNameWithoutExtension(package)["Faro.Runtime.".Length..];
        Directory.CreateDirectory(Path.Combine(dir, ".faro", "packages"));
        var vendored = Path.Combine(dir, ".faro", "packages", Path.GetFileName(package));
        if (!File.Exists(vendored)) File.Copy(package, vendored);

        WriteNew(Path.Combine(dir, ProjectFile), new JsonObject { ["name"] = name, ["language"] = "CSharp", ["runtime"] = version }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
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
                    <Content Include="UI/**;Bindings/**;Assets/**" CopyToOutputDirectory="PreserveNewest" />
                    <!-- dotnet watch restarts the app when the UI graph or bindings change (spec §9). -->
                    <Watch Include="UI/**;Bindings/**" />
                  </ItemGroup>
                </Project>

                """);
        WriteNew(Path.Combine(dir, "Program.cs"), $"Faro.Runtime.FaroApp.Run(args, typeof(Program).Assembly, \"{startScreen}\");\n");
        WriteNew(Path.Combine(dir, ".gitignore"), "bin/\nobj/\n");
    }

    static void WriteNew(string path, string text)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Most recent first, without duplicates, at most 10 (kept in the app settings).</summary>
    public static void Remember(string dir)
    {
        var settings = FaroSettings.Current;
        settings.RecentProjects = [.. new[] { Path.GetFullPath(dir) }.Concat(settings.RecentProjects).Distinct().Take(10)];
        settings.Save();
    }
}
