using Avalonia.Threading;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// The open project plus its Roslyn registry and binding issues. Reloads whenever a file under
/// UI/, Bindings/ or Source/ is saved: saved content only, never unsaved buffers (spec §6).
/// </summary>
public static class Workspace
{
    public static string Root { get; private set; } = "";

    /// <summary>Restricted mode (untrusted project): no restore, language server, Script previews or Run.</summary>
    public static bool Trusted { get; private set; }
    public static FaroProject? Project { get; private set; }
    public static List<RegistryMember> Registry { get; private set; } = [];
    public static List<string> ScriptClasses { get; private set; } = [];
    public static List<BindingIssue> Issues { get; private set; } = [];
    public static string? LoadError { get; private set; }
    public static event Action? Changed;

    static FileSystemWatcher? watcher;

    /// <param name="report">Startup progress (step text, percent) for the splash screen.</param>
    public static void Open(string root, Action<string, double>? report = null)
    {
        Root = root;
        Trusted = ProjectSetup.IsTrusted(root);
        UiBuilder.ScriptFactory = Trusted ? name => ScriptPreview.Build(root, name) : null;
        Reload(report);
        watcher?.Dispose();
        if (!Directory.Exists(root)) return;
        watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        FileSystemEventHandler onChange = (_, e) =>
        {
            var rel = Path.GetRelativePath(root, e.FullPath);
            if (rel.StartsWith("UI") || rel.StartsWith("Bindings") || rel.StartsWith("Source"))
                Dispatcher.UIThread.Post(() => Reload());
        };
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Deleted += onChange;
        watcher.Renamed += (s, e) => onChange(s, e);
    }

    // Run (spec §9): `dotnet watch run` with its output in the console and its build errors in Problems.

    static System.Diagnostics.Process? app;
    const int MaxOutputLines = 5000;

    public static bool Running => app is { HasExited: false };
    public static List<string> Output { get; } = [];
    public static List<BuildError> BuildErrors { get; private set; } = [];
    public static event Action<string>? OutputLine;
    /// <summary>Started/stopped, or the build errors changed.</summary>
    public static event Action? RunChanged;

    /// <summary>Runs the project with hot reload; code saves hot-reload, UI graph changes restart (dotnet watch).</summary>
    public static void Run()
    {
        if (Running || !Trusted) return;
        Output.Clear();
        var start = new System.Diagnostics.ProcessStartInfo("dotnet", ["watch", "run", "--non-interactive"])
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = new System.Diagnostics.Process { StartInfo = start, EnableRaisingEvents = true };
        System.Diagnostics.DataReceivedEventHandler read = (_, e) => { if (e.Data is { } line) Dispatcher.UIThread.Post(() => Add(line)); };
        process.OutputDataReceived += read;
        process.ErrorDataReceived += read;
        process.Exited += (_, _) => Dispatcher.UIThread.Post(() => { Add("[Stopped]"); RunChanged?.Invoke(); });
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        app = process;
        RunChanged?.Invoke();
    }

    /// <summary>Stops the app together with dotnet watch.</summary>
    public static void Stop()
    {
        if (Running) app!.Kill(entireProcessTree: true);
    }

    static void Add(string line)
    {
        if (Output.Count >= MaxOutputLines) Output.RemoveAt(0);
        Output.Add(line);
        OutputLine?.Invoke(line);
        if (BuildError.Parse(line, out var building) is { } error)
        {
            if (!BuildErrors.Contains(error)) { BuildErrors.Add(error); RunChanged?.Invoke(); } // MSBuild repeats errors in its summary
        }
        else if (building && BuildErrors.Count > 0) { BuildErrors = []; RunChanged?.Invoke(); }
        if (line.Contains("Build succeeded") || line.Contains("Hot reload succeeded"))
        {
            appliedAt = DateTime.UtcNow;
            CheckBuilt();
            RunChanged?.Invoke();
        }
    }

    static DateTime appliedAt;

    /// <summary>Saved code newer than the last build or hot reload (spec §11.5): the registry may list members the app doesn't have yet.</summary>
    public static bool Unbuilt { get; private set; }

    static void CheckBuilt()
    {
        var source = Path.Combine(Root, "Source");
        var bin = Path.Combine(Root, "bin");
        var edited = Directory.Exists(source) ? Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max() : default;
        var built = Directory.Exists(bin)
            ? Directory.EnumerateFiles(Root, "*.csproj").SelectMany(p => Directory.EnumerateFiles(bin, Path.GetFileNameWithoutExtension(p) + ".dll", SearchOption.AllDirectories)).Select(File.GetLastWriteTimeUtc).Append(appliedAt).Max()
            : appliedAt;
        Unbuilt = edited > built;
    }

    public static void Reload(Action<string, double>? report = null)
    {
        try
        {
            report?.Invoke("Loading UI graphs and bindings…", 15);
            Project = FaroProject.Load(Root);
            report?.Invoke("Analyzing Source/ with Roslyn…", 35);
            Registry = Editor.Registry.Scan(Path.Combine(Root, "Source"));
            report?.Invoke("Checking bindings…", 60);
            ScriptClasses = Editor.Registry.ScriptClasses(Path.Combine(Root, "Source"));
            Issues = BindingCheck.Check(Project, Registry, ScriptClasses);
            CheckBuilt();
            LoadError = null;
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or InvalidDataException)
        {
            // A half-written file: keep the last good state and show why.
            LoadError = e.Message;
            Log.Error("Loading the project", e);
        }
        Changed?.Invoke();
    }
}

/// <summary>A compiler error from the run output: "path(line,col): error CS1002: message [project]".</summary>
public sealed partial record BuildError(string File, int Line, int Column, string Code, string Message)
{
    /// <param name="building">A new build started (so the previous errors are gone).</param>
    public static BuildError? Parse(string line, out bool building)
    {
        line = WatchPrefix().Replace(line, "");
        building = line.StartsWith("Building ");
        var m = ErrorLine().Match(line);
        return m.Success ? new(m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value, m.Groups[5].Value) : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^dotnet watch \S+ ")]
    private static partial System.Text.RegularExpressions.Regex WatchPrefix();

    [System.Text.RegularExpressions.GeneratedRegex(@"^(.+?)\((\d+),(\d+)\): error (\w+): (.*?)(?: \[[^\]]*\])?$")]
    private static partial System.Text.RegularExpressions.Regex ErrorLine();
}
