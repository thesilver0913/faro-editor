using System.Reflection;
using System.Runtime.Loader;
using Avalonia.Controls;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// Script nodes on the canvas: their code-built control from the project's last build (bin/**/&lt;project&gt;.dll),
/// loaded in a collectible context that shares Avalonia and Faro.Runtime with the editor. Runs the user's own
/// code at design time, like a designer does. ponytail: a Build() that never returns hangs the canvas.
/// </summary>
public static class ScriptPreview
{
    static Context? context;
    static Assembly? assembly;
    static (string Path, DateTime Stamp) loaded;

    /// <summary>The newest build of the project, if any.</summary>
    static (string Path, DateTime Stamp) Latest(string root)
    {
        var bin = Path.Combine(root, "bin");
        return !Directory.Exists(bin) ? default
            : Directory.EnumerateFiles(root, "*.csproj").SelectMany(p => Directory.EnumerateFiles(bin, Path.GetFileNameWithoutExtension(p) + ".dll", SearchOption.AllDirectories))
                .Select(dll => (dll, File.GetLastWriteTimeUtc(dll))).MaxBy(d => d.Item2);
    }

    /// <summary>The build changed since the canvas last loaded it (redraw).</summary>
    public static bool Outdated(string root) => assembly is not null && Latest(root) != loaded;

    /// <summary>UiBuilder.ScriptFactory for the canvas: null (placeholder) until a build has the class.</summary>
    public static Control Build(string root, string name)
    {
        var type = (Load(root) ?? throw new InvalidOperationException("Not built yet: Run to preview it.")).GetType(name)
            ?? throw new InvalidOperationException("Not in the last build: Run to preview it.");
        if (!typeof(FaroScript).IsAssignableFrom(type)) throw new InvalidOperationException($"{name} doesn't derive from FaroScript.");
        return ((FaroScript)Activator.CreateInstance(type)!).Build();
    }

    /// <summary>The project's last build (also the canvas's data preview), or null before the first build.</summary>
    public static Assembly? Load(string root)
    {
        var latest = Latest(root);
        if (latest.Path is null) return null;
        if (latest != loaded)
        {
            context?.Unload();
            context = new Context(Path.GetDirectoryName(latest.Path)!);
            assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(latest.Path))); // no file lock: the next build can overwrite it
            loaded = latest;
        }
        return assembly;
    }

    sealed class Context(string dir) : AssemblyLoadContext(isCollectible: true)
    {
        // Avalonia, Faro.Runtime and the framework come from the editor, so the script's controls are the editor's types.
        protected override Assembly? Load(AssemblyName name) =>
            Default.Assemblies.Any(a => a.GetName().Name == name.Name) || Path.Combine(dir, name.Name + ".dll") is not { } path || !File.Exists(path)
                ? null
                : LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
    }
}
