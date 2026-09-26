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
    public static FaroProject? Project { get; private set; }
    public static List<RegistryMember> Registry { get; private set; } = [];
    public static List<BindingIssue> Issues { get; private set; } = [];
    public static string? LoadError { get; private set; }
    public static event Action? Changed;

    static FileSystemWatcher? watcher;

    public static void Open(string root)
    {
        Root = root;
        Reload();
        watcher?.Dispose();
        if (!Directory.Exists(root)) return;
        watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        FileSystemEventHandler onChange = (_, e) =>
        {
            var rel = Path.GetRelativePath(root, e.FullPath);
            if (rel.StartsWith("UI") || rel.StartsWith("Bindings") || rel.StartsWith("Source"))
                Dispatcher.UIThread.Post(Reload);
        };
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Deleted += onChange;
        watcher.Renamed += (s, e) => onChange(s, e);
    }

    public static void Reload()
    {
        try
        {
            Project = FaroProject.Load(Root);
            Registry = Editor.Registry.Scan(Path.Combine(Root, "Source"));
            Issues = BindingCheck.Check(Project, Registry);
            LoadError = null;
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or InvalidDataException)
        {
            // A half-written file: keep the last good state and show why.
            LoadError = e.Message;
        }
        Changed?.Invoke();
    }
}
