using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// Java projects (faro.json "language": "Java"): JavaFX apps built with Maven. The same UI/ and Bindings/ files as C#;
/// the Java runtime binder (src/Faro.Runtime.Java) is vendored as sources in .faro/runtime-java and compiled with the app.
/// Bind targets are "package.Class.member": a public method, or a bean property (getName()/isName() → "name").
/// </summary>
public static partial class JavaProject
{
    public const string Language = "Java";
    const string RuntimeFolder = ".faro/runtime-java";

    public static bool Is(string root) =>
        File.Exists(Path.Combine(root, ProjectSetup.ProjectFile))
        && (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(root, ProjectSetup.ProjectFile)))?["language"] == Language;

    /// <summary>`mvn javafx:run` (Maven's launcher is mvn.cmd on Windows).</summary>
    public static (string File, string[] Args) RunCommand => (OperatingSystem.IsWindows() ? "mvn.cmd" : "mvn", ["javafx:run"]);

    // Registry (spec §3) from sources only, so unbuilt code works. ponytail: a declaration scanner, not a Java parser;
    // comments, strings and nested classes are skipped. Swap in a real parser if projects hit its limits.

    [GeneratedRegex(@"^\s*package\s+([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex PackageLine();

    [GeneratedRegex(@"\bpublic\s+(?:(?:abstract|final|sealed|non-sealed|strictfp)\s+)*class\s+(?<name>\w+)(?:\s*<[^{]*?>)?(?:\s+extends\s+(?<base>[\w.]+))?")]
    private static partial Regex ClassHeader();

    [GeneratedRegex(@"(?:^|\s)(?<mods>(?:(?:public|protected|private|static|final|abstract|synchronized|native|default)\s+)+)(?:<[^>]*>\s*)?(?<type>[\w.$\[\]<>?, ]+?)\s+(?<name>\w+)\s*\((?<params>[^)]*)\)\s*(?:throws\s+[\w.,\s]+)?$", RegexOptions.Singleline)]
    private static partial Regex MemberHeader();

    public static IEnumerable<RegistryMember> Parse(string code) => Classes(code).SelectMany(c => c.Members);

    public static IEnumerable<string> ClassNames(string code) => Classes(code).Select(c => c.Name);

    public static IEnumerable<string> ScriptClasses(string code) =>
        Classes(code).Where(c => c.Base.Split('.')[^1] == "FaroScript").Select(c => c.Name);

    static List<(string Name, string Base, List<RegistryMember> Members)> Classes(string code)
    {
        var text = Strip(code);
        var package = PackageLine().Match(text) is { Success: true } p ? p.Groups[1].Value + "." : "";
        var classes = new List<(string, string, List<RegistryMember>)>();
        List<RegistryMember>? members = null;
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is not ('{' or ';' or '}')) continue;
            var header = text[start..i].Trim();
            if (depth == 0 && ch == '{')
            {
                var m = ClassHeader().Match(header);
                members = m.Success ? [] : null;
                if (m.Success) classes.Add((package + m.Groups["name"].Value, m.Groups["base"].Value, members!));
            }
            else if (depth == 1 && ch != '}' && members is not null && Member(classes[^1].Item1, header) is { } member)
                members.Add(member);
            depth += ch == '{' ? 1 : ch == '}' ? -1 : 0;
            start = i + 1;
        }
        return classes;
    }

    static RegistryMember? Member(string cls, string header)
    {
        if (header.Contains("@Override") || MemberHeader().Match(header) is not { Success: true } m) return null;
        var mods = m.Groups["mods"].Value;
        var (type, name, parameters) = (m.Groups["type"].Value.Trim(), m.Groups["name"].Value, m.Groups["params"].Value.Trim());
        if (!Regex.IsMatch(mods, @"\bpublic\b") || name == "main" && mods.Contains("static")) return null;
        if (parameters.Length == 0 && type != "void" && Regex.Match(name, @"^(?:get|is)([A-Z]\w*)$") is { Success: true } getter)
        {
            var property = char.ToLowerInvariant(getter.Groups[1].Value[0]) + getter.Groups[1].Value[1..];
            return new($"{cls}.{property}", false, $"{type} {property}");
        }
        return Regex.IsMatch(name, @"^set[A-Z]") && parameters.Length > 0 && !parameters.Contains(',')
            ? null // a property's setter
            : new($"{cls}.{name}", true, $"{type} {name}({parameters})");
    }

    /// <summary>Comments and string/char literals blanked (same length), so braces and semicolons in them don't count.</summary>
    static string Strip(string code)
    {
        var s = code.ToCharArray();
        void Blank(int from, int to) { for (var k = from; k < Math.Min(to, s.Length); k++) if (s[k] != '\n') s[k] = ' '; }
        for (var i = 0; i < code.Length; i++)
        {
            int end;
            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/') end = code.IndexOf('\n', i) is var n && n < 0 ? code.Length : n;
            else if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '*') end = code.IndexOf("*/", i + 2, StringComparison.Ordinal) is var c && c < 0 ? code.Length : c + 2;
            else if (code.AsSpan(i).StartsWith("\"\"\"")) end = code.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal) is var t && t < 0 ? code.Length : t + 3;
            else if (code[i] is '"' or '\'')
            {
                end = i + 1;
                while (end < code.Length && code[end] != code[i] && code[end] != '\n') end += code[end] == '\\' ? 2 : 1;
                end = Math.Min(end + 1, code.Length);
            }
            else continue;
            Blank(i, end);
            i = end - 1;
        }
        return new string(s);
    }

    // Project files

    /// <summary>pom.xml (JavaFX + the vendored runtime as a source folder), Main.java, the runtime sources and .gitignore.</summary>
    public static void Initialize(string dir, string name, string version, string appDir)
    {
        Vendor(dir, appDir);
        ProjectSetup.WriteNew(Path.Combine(dir, "pom.xml"), $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- Generated by Faro. Language: Java (fixed for this project). Run: mvn javafx:run -->
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>app</groupId>
              <artifactId>{{name}}</artifactId>
              <version>1.0</version>
              <properties>
                <maven.compiler.release>21</maven.compiler.release>
                <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
                <javafx.version>21.0.5</javafx.version>
                <faro.runtime>{{RuntimeFolder}}</faro.runtime>
              </properties>
              <dependencies>
                <dependency>
                  <groupId>org.openjfx</groupId>
                  <artifactId>javafx-controls</artifactId>
                  <version>${javafx.version}</version>
                </dependency>
              </dependencies>
              <build>
                <sourceDirectory>Source</sourceDirectory>
                <plugins>
                  <plugin>
                    <groupId>org.codehaus.mojo</groupId>
                    <artifactId>build-helper-maven-plugin</artifactId>
                    <version>3.6.0</version>
                    <executions>
                      <execution>
                        <id>faro-runtime</id>
                        <phase>generate-sources</phase>
                        <goals><goal>add-source</goal></goals>
                        <configuration><sources><source>${faro.runtime}</source></sources></configuration>
                      </execution>
                    </executions>
                  </plugin>
                  <plugin>
                    <groupId>org.openjfx</groupId>
                    <artifactId>javafx-maven-plugin</artifactId>
                    <version>0.0.8</version>
                    <configuration><mainClass>Main</mainClass></configuration>
                  </plugin>
                </plugins>
              </build>
            </project>

            """);
        ProjectSetup.WriteNew(Path.Combine(dir, "Source", "Main.java"), """
            public class Main {
                public static void main(String[] args) {
                    faro.runtime.FaroApp.run(args, Main.class); // start screen: faro.json
                }
            }

            """);
        ProjectSetup.WriteNew(Path.Combine(dir, ".gitignore"), "target/\n");
    }

    /// <summary>Copies the editor's Java runtime sources into the project (replacing an older copy).</summary>
    public static void Vendor(string dir, string appDir)
    {
        var from = Path.Combine(appDir, "runtime-java");
        var to = Path.Combine(dir, RuntimeFolder);
        if (Directory.Exists(to)) Directory.Delete(to, true);
        foreach (var file in Directory.EnumerateFiles(from, "*.java", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>The vendored runtime's version (faro.json "runtime"); null without a vendored copy (e.g. the in-repo sample).</summary>
    public static string? ProjectRuntime(string dir) =>
        Directory.Exists(Path.Combine(dir, RuntimeFolder)) && JsonNode.Parse(File.ReadAllText(Path.Combine(dir, ProjectSetup.ProjectFile)))?["runtime"] is { } v
            && ProjectSetup.VersionKey((string)v!) is not null ? (string)v! : null;

    /// <summary>A new class file for Source/: the package follows the folder (Java requires it).</summary>
    public static string ClassFile(string relativeFolder, string name)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z_$][\w$]*$")) throw new ArgumentException($"'{name}' is not a valid class name.");
        var package = string.Join('.', relativeFolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
        return (package.Length > 0 ? $"package {package};\n\n" : "") + $"import faro.runtime.FaroObject;\n\npublic class {name} extends FaroObject {{\n}}\n";
    }

    /// <summary>The AI Chat rules for Java (spec §8), in place of the C# ones.</summary>
    public static string PromptRules(string attribute, IEnumerable<string> screens) => $"""
        You write Java for a Faro project: a JavaFX app whose UI (XML screens) is bound at runtime to public members
        of classes in Source/, addressed by the string "package.Class.member".

        Rules:
        - Event bindings (e.g. Click) call public parameterless methods ("pkg.Class.submit"). Property bindings use bean
          properties: getName()/isName() (and setName(...) for TwoWay), addressed as "pkg.Class.name".
        - A list (repeatable instance) binds prop "Items" to a List property; call changed("orders") after changing it to redraw.
          Binds to nodes inside the list ("orderList/name") then target properties of the item class for each row.
        - Classes are public, top-level, one per file, in the package matching their folder under Source/, and extend
          faro.runtime.FaroObject. After changing state, call changed("name", "otherDependentProperty") so bindings refresh.
        - A Script node's class extends faro.runtime.FaroScript and implements `@Override public javafx.scene.Node build()`,
          returning the JavaFX node (look and behaviour) shown in the node's place.
        - {attribute}
        - Screens: {string.Join(", ", screens)}. Navigate with faro.runtime.FaroApp.navigate("ScreenId").
        - When changing an existing class, keep every existing member unless asked to remove it.

        Output format: for every file you create or change, write a line `File: Source/<package folders>/<Name>.java` followed by a
        ```java block with the COMPLETE new content of that file. Only files under Source/. Keep explanations short.
        """;

    /// <summary>
    /// JavaFX CSS for Material 3 (.faro/design.css, loaded by the Java runtime): the same color roles from the seed as the
    /// Avalonia styles, with shapes and type scale. Null for Fluent (JavaFX keeps its own look). "System" renders light.
    /// ponytail: no spring morph on press (JavaFX CSS has no transitions); the corner just snaps.
    /// </summary>
    public static string? DesignCss(AppDesign design)
    {
        if (design.Language != "Material3") return null;
        var scheme = AppDesign.Scheme(MaterialColorUtilities.Palettes.CorePalette.Of(Avalonia.Media.Color.Parse(design.SeedColor).ToUInt32(),
            MaterialColorUtilities.Palettes.Style.TonalSpot), dark: design.Theme == "Dark");
        var css = new StringBuilder("/* Generated by Faro from faro.json \"design\" (Material 3 Expressive). Rewritten on change: don't edit. */\n");
        foreach (var weight in FontWeights) css.Append($"@font-face {{ font-family: 'Google Sans Flex'; src: url('fonts/GoogleSansFlex-{weight}.ttf'); }}\n");
        css.Append(".root {\n    -fx-font-family: 'Google Sans Flex';\n");
        foreach (var key in scheme.Keys.OfType<string>().Where(k => k.StartsWith("M3")))
            css.Append($"    m3-{Kebab(key[2..])}: #{((Avalonia.Media.ISolidColorBrush)scheme[key]!).Color.ToUInt32() & 0xFFFFFF:x6};\n");
        css.Append("""
                -fx-font-size: 16px;
                -fx-accent: m3-primary;
                -fx-focus-color: m3-primary;
                -fx-faint-focus-color: transparent;
                -fx-text-base-color: m3-on-surface;
            }
            .faro-screen { -fx-background-color: m3-surface; }
            .label { -fx-text-fill: m3-on-surface; }

            .button {
                -fx-background-color: m3-primary; -fx-text-fill: m3-on-primary; -fx-background-radius: 20; -fx-border-radius: 20;
                -fx-min-height: 40; -fx-padding: 0 16; -fx-font-size: 14px; -fx-font-weight: bold; -fx-cursor: hand;
            }
            .button:hover { -fx-background-color: derive(m3-primary, 12%); }
            .button.m3-variant-tonal { -fx-background-color: m3-secondary-container; -fx-text-fill: m3-on-secondary-container; }
            .button.m3-variant-tonal:hover { -fx-background-color: derive(m3-secondary-container, -6%); }
            .button.m3-variant-outlined { -fx-background-color: transparent; -fx-text-fill: m3-on-surface-variant; -fx-border-color: m3-outline-variant; }
            .button.m3-variant-text { -fx-background-color: transparent; -fx-text-fill: m3-primary; -fx-padding: 0 12; }
            .button.m3-variant-outlined:hover, .button.m3-variant-text:hover { -fx-background-color: m3-surface-container; }
            .button.m3-variant-elevated { -fx-background-color: m3-surface-container-low; -fx-text-fill: m3-primary; -fx-effect: dropshadow(gaussian, rgba(0,0,0,0.3), 4, 0, 0, 1); }
            .button.m3-size-xs { -fx-min-height: 32; -fx-padding: 0 12; -fx-background-radius: 16; -fx-border-radius: 16; }
            .button.m3-size-m { -fx-min-height: 56; -fx-padding: 0 24; -fx-font-size: 16px; -fx-background-radius: 28; -fx-border-radius: 28; }
            .button.m3-size-l { -fx-min-height: 96; -fx-padding: 0 48; -fx-font-size: 24px; -fx-font-weight: normal; -fx-background-radius: 48; -fx-border-radius: 48; }
            .button.m3-size-xl { -fx-min-height: 136; -fx-padding: 0 64; -fx-font-size: 32px; -fx-font-weight: normal; -fx-background-radius: 68; -fx-border-radius: 68; }
            .button.m3-shape-square { -fx-background-radius: 12; -fx-border-radius: 12; }
            .button.m3-shape-square.m3-size-m { -fx-background-radius: 16; -fx-border-radius: 16; }
            .button.m3-shape-square.m3-size-l, .button.m3-shape-square.m3-size-xl { -fx-background-radius: 28; -fx-border-radius: 28; }
            .button:pressed, .button.m3-size-xs:pressed { -fx-background-radius: 8; -fx-border-radius: 8; }
            .button.m3-size-m:pressed { -fx-background-radius: 12; -fx-border-radius: 12; }
            .button.m3-size-l:pressed, .button.m3-size-xl:pressed { -fx-background-radius: 16; -fx-border-radius: 16; }
            .button:disabled { -fx-opacity: 0.38; }

            .text-field {
                -fx-background-color: m3-on-surface-variant, m3-surface-container-highest; -fx-background-insets: 0, 0 0 1 0;
                -fx-background-radius: 4 4 0 0; -fx-min-height: 56; -fx-padding: 16; -fx-text-fill: m3-on-surface;
                -fx-prompt-text-fill: m3-on-surface-variant; -fx-highlight-fill: m3-primary-container; -fx-highlight-text-fill: m3-on-primary-container;
            }
            .text-field:focused { -fx-background-color: m3-primary, m3-surface-container-highest; -fx-background-insets: 0, 0 0 2 0; }
            .text-field.m3-variant-outlined { -fx-background-color: m3-outline, m3-surface; -fx-background-insets: 0, 1; -fx-background-radius: 4; }
            .text-field.m3-variant-outlined:focused { -fx-background-color: m3-primary, m3-surface; -fx-background-insets: 0, 2; }

            .label.m3-type-displaylarge { -fx-font-size: 57px; }
            .label.m3-type-displaymedium { -fx-font-size: 45px; }
            .label.m3-type-displaysmall { -fx-font-size: 36px; }
            .label.m3-type-headlinelarge { -fx-font-size: 32px; }
            .label.m3-type-headlinemedium { -fx-font-size: 28px; }
            .label.m3-type-headlinesmall { -fx-font-size: 24px; }
            .label.m3-type-titlelarge { -fx-font-size: 22px; }
            .label.m3-type-titlemedium { -fx-font-size: 16px; -fx-font-weight: bold; }
            .label.m3-type-titlesmall { -fx-font-size: 14px; -fx-font-weight: bold; }
            .label.m3-type-bodylarge { -fx-font-size: 16px; }
            .label.m3-type-bodymedium { -fx-font-size: 14px; }
            .label.m3-type-bodysmall { -fx-font-size: 12px; }
            .label.m3-type-labellarge { -fx-font-size: 14px; -fx-font-weight: bold; }
            .label.m3-type-labelmedium { -fx-font-size: 12px; -fx-font-weight: bold; }
            .label.m3-type-labelsmall { -fx-font-size: 11px; -fx-font-weight: bold; }
            .label.m3-emphasized-true { -fx-font-weight: bold; }
            .label.m3-color-primary { -fx-text-fill: m3-primary; }
            .label.m3-color-secondary { -fx-text-fill: m3-secondary; }
            .label.m3-color-tertiary { -fx-text-fill: m3-tertiary; }
            .label.m3-color-error { -fx-text-fill: m3-error; }
            .label.m3-color-onsurfacevariant { -fx-text-fill: m3-on-surface-variant; }

            .m3-surface-surface { -fx-background-color: m3-surface; }
            .m3-surface-lowest { -fx-background-color: m3-surface-container-lowest; }
            .m3-surface-low { -fx-background-color: m3-surface-container-low; }
            .m3-surface-container { -fx-background-color: m3-surface-container; }
            .m3-surface-high { -fx-background-color: m3-surface-container-high; }
            .m3-surface-highest { -fx-background-color: m3-surface-container-highest; }
            .m3-surface-primary { -fx-background-color: m3-primary-container; }
            .m3-surface-primary .label { -fx-text-fill: m3-on-primary-container; }
            .m3-surface-secondary { -fx-background-color: m3-secondary-container; }
            .m3-surface-secondary .label { -fx-text-fill: m3-on-secondary-container; }
            .m3-surface-tertiary { -fx-background-color: m3-tertiary-container; }
            .m3-surface-tertiary .label { -fx-text-fill: m3-on-tertiary-container; }
            .m3-corner-xs { -fx-background-radius: 4; }
            .m3-corner-s { -fx-background-radius: 8; }
            .m3-corner-m { -fx-background-radius: 12; }
            .m3-corner-l { -fx-background-radius: 16; }
            .m3-corner-xl { -fx-background-radius: 28; }
            .m3-corner-full { -fx-background-radius: 9999; }
            .m3-elevation-1 { -fx-effect: dropshadow(gaussian, rgba(0,0,0,0.3), 4, 0, 0, 1); }
            .m3-elevation-2 { -fx-effect: dropshadow(gaussian, rgba(0,0,0,0.3), 8, 0, 0, 2); }
            .m3-elevation-3 { -fx-effect: dropshadow(gaussian, rgba(0,0,0,0.3), 12, 0, 0, 4); }
            .m3-elevation-4 { -fx-effect: dropshadow(gaussian, rgba(0,0,0,0.3), 14, 0, 0, 6); }
            .m3-elevation-5 { -fx-effect: dropshadow(gaussian, rgba(0,0,0,0.3), 18, 0, 0, 8); }

            """);
        return css.ToString();
    }

    static readonly string[] FontWeights = ["Regular", "Medium", "Bold"];

    static string Kebab(string role) => Regex.Replace(role, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();

    /// <summary>Keeps .faro/design.css in step with faro.json (written only when it changes; removed for Fluent).</summary>
    public static void WriteDesignCss(string dir, AppDesign design)
    {
        var path = Path.Combine(dir, ".faro", "design.css");
        if (DesignCss(design) is not { } css) { File.Delete(path); return; }
        // The typeface next to it (the CSS loads fonts/GoogleSansFlex-*.ttf), so the project runs anywhere.
        foreach (var weight in FontWeights)
        {
            var font = Path.Combine(dir, ".faro", "fonts", $"GoogleSansFlex-{weight}.ttf");
            var bundled = Path.Combine(AppContext.BaseDirectory, "fonts", $"GoogleSansFlex-{weight}.ttf");
            if (File.Exists(font) || !File.Exists(bundled)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(font)!);
            File.Copy(bundled, font);
        }
        if (File.Exists(path) && File.ReadAllText(path) == css) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, css);
    }

    // Android APK (File › Build Android APK): GluonFX turns Source/ and the runtime into a native Android app with Gluon's
    // GraalVM. .faro/android/pom.xml is generated from the project's pom (properties and dependencies, runtime path re-rooted);
    // the project files ride along as resources under faro/ with an index.txt (the runtime reads them there, see FaroApp.url).

    // Works with Gluon's latest public GraalVM (gluon-23+25.1-dev) and Maven 3.9: 1.0.24 rejects Maven 3.9, and from
    // 1.0.29 (substrate 0.0.69) the plugin passes -H:+ForceNoROSectionRelocations, which that GraalVM doesn't know.
    const string GluonFxVersion = "1.0.25";
    public const string GraalVmDownload = "https://github.com/gluonhq/graal/releases";

    /// <summary>Writes .faro/android (pom.xml and resources/faro/…); returns the pom's path.</summary>
    public static string WriteAndroid(string root)
    {
        var head = AndroidApk.Head(root);
        var pom = System.Xml.Linq.XDocument.Load(Path.Combine(root, "pom.xml"));
        var ns = pom.Root!.Name.Namespace;
        var name = (string?)pom.Root.Element(ns + "artifactId") ?? "app";
        var properties = new System.Xml.Linq.XElement(pom.Root.Element(ns + "properties") ?? new System.Xml.Linq.XElement(ns + "properties"));
        var runtime = properties.Element(ns + "faro.runtime");
        runtime?.SetValue(Path.GetRelativePath(head, Path.Combine(root, runtime.Value)).Replace('\\', '/'));

        WriteDesignCss(root, FaroProject.Load(root).Design);
        var resources = Path.Combine(head, "resources", "faro");
        if (Directory.Exists(resources)) Directory.Delete(resources, true);
        var files = new[] { "UI", "Bindings", "Assets", Path.Combine(".faro", "fonts") }
            .Where(d => Directory.Exists(Path.Combine(root, d)))
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(root, f))
            .Prepend(ProjectSetup.ProjectFile).Append(Path.Combine(".faro", "design.css"))
            .Where(f => File.Exists(Path.Combine(root, f))).ToList();
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(resources, file))!);
            File.Copy(Path.Combine(root, file), Path.Combine(resources, file));
        }
        File.WriteAllLines(Path.Combine(resources, "index.txt"), files.Select(f => f.Replace('\\', '/')));

        var source = Path.Combine(root, "Source");
        IEnumerable<string> classes = Directory.Exists(source)
            ? Directory.EnumerateFiles(source, "*.java", SearchOption.AllDirectories).SelectMany(f => ClassNames(File.ReadAllText(f))).Distinct().Order()
            : [];
        var path = Path.Combine(head, "pom.xml");
        File.WriteAllText(path, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- Generated by Faro for File › Build Android APK (GluonFX); rewritten on every build. -->
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>app</groupId>
              <artifactId>{{name}}</artifactId>
              <version>1.0</version>
              {{properties.ToString().Replace(" xmlns=\"http://maven.apache.org/POM/4.0.0\"", "")}}
              {{pom.Root.Element(ns + "dependencies")?.ToString().Replace(" xmlns=\"http://maven.apache.org/POM/4.0.0\"", "")}}
              <build>
                <sourceDirectory>../../Source</sourceDirectory>
                <resources><resource><directory>resources</directory></resource></resources>
                <plugins>
                  <plugin>
                    <groupId>org.codehaus.mojo</groupId>
                    <artifactId>build-helper-maven-plugin</artifactId>
                    <version>3.6.0</version>
                    <executions>
                      <execution>
                        <id>faro-runtime</id>
                        <phase>generate-sources</phase>
                        <goals><goal>add-source</goal></goals>
                        <configuration><sources><source>${faro.runtime}</source></sources></configuration>
                      </execution>
                    </executions>
                  </plugin>
                  <plugin>
                    <groupId>com.gluonhq</groupId>
                    <artifactId>gluonfx-maven-plugin</artifactId>
                    <version>{{GluonFxVersion}}</version>
                    <configuration>
                      <target>android</target>
                      <mainClass>Main</mainClass>
                      <appIdentifier>{{AndroidApk.ApplicationId(name)}}</appIdentifier>
                      <releaseConfiguration><appLabel>{{name}}</appLabel></releaseConfiguration>
                      <!-- Bindings reach these classes by reflection; native images need them listed. -->
                      <reflectionList>
            {{string.Concat(classes.Select(c => $"            <list>{c}</list>\n"))}}          </reflectionList>
                      <resourcesList><list>faro/.*</list></resourcesList>
                    </configuration>
                  </plugin>
                </plugins>
              </build>
            </project>

            """);
        return path;
    }

    /// <summary>Builds the APK with GluonFX (Linux only; needs GRAALVM_HOME = Gluon's GraalVM) and copies it to dist/.</summary>
    public static async Task<string?> BuildApkAsync(string root, Action<string> output, CancellationToken cancel)
    {
        if (!OperatingSystem.IsLinux())
        {
            output(L.T("Java Android builds run on Linux only (GluonFX): use Linux, WSL or CI (`Faro.Editor --build-apk <folder>`)."));
            return null;
        }
        if (Environment.GetEnvironmentVariable("GRAALVM_HOME") is null)
        {
            output(L.F("Set GRAALVM_HOME to Gluon's GraalVM ({0}), then build again.", GraalVmDownload));
            return null;
        }
        var pom = WriteAndroid(root);
        var target = Path.Combine(AndroidApk.Head(root), "target");
        if (Directory.Exists(target)) Directory.Delete(target, true);
        output(L.T("Building with GluonFX (the first build downloads the Android SDK and takes several minutes)…"));
        if (await AndroidApk.Exec("mvn", ["-B", "-f", pom, "gluonfx:build", "gluonfx:package"], output, cancel) != 0) return null;
        return Directory.Exists(target) && Directory.EnumerateFiles(target, "*.apk", SearchOption.AllDirectories).MaxBy(File.GetLastWriteTimeUtc) is { } apk
            ? AndroidApk.CopyToDist(root, apk, Path.GetFileName(Path.TrimEndingDirectorySeparator(root)))
            : null;
    }
}
