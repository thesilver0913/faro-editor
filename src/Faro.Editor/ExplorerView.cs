using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Faro.Editor;

/// <summary>
/// VS Code-style explorer over the project folders (spec §4). Click opens: screens on the canvas, C# in the
/// code editor. Right-click creates, renames and deletes. Screen/component file operations follow their
/// references and are UI history steps; other file operations are plain file system changes.
/// </summary>
public sealed class ExplorerView : UserControl
{
    static readonly string[] Folders = ["UI", "Source", "Bindings", "Assets"];
    static readonly HashSet<string> expanded = [];

    readonly TreeView tree = new();
    readonly DispatcherTimer rebuild = new() { Interval = TimeSpan.FromMilliseconds(300) };
    FileSystemWatcher? watcher;

    public ExplorerView()
    {
        Content = tree;
        rebuild.Tick += (_, _) => { rebuild.Stop(); Build(); };
        tree.SelectionChanged += (_, _) => { if (tree.SelectedItem is TreeViewItem { Tag: string path }) Open(path); };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Build();
        if (!Directory.Exists(Workspace.Root)) return;
        watcher = new FileSystemWatcher(Workspace.Root) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        FileSystemEventHandler changed = (_, a) =>
        {
            if (!a.FullPath.Contains($"{Path.DirectorySeparatorChar}bin") && !a.FullPath.Contains($"{Path.DirectorySeparatorChar}obj"))
                Dispatcher.UIThread.Post(() => { rebuild.Stop(); rebuild.Start(); }); // debounce bursts of events
        };
        watcher.Created += changed;
        watcher.Deleted += changed;
        watcher.Renamed += (s, a) => changed(s, a);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        watcher?.Dispose();
        watcher = null;
        base.OnDetachedFromVisualTree(e);
    }

    void Build() => tree.ItemsSource = Folders.Select(f => Path.Combine(Workspace.Root, f)).Where(Directory.Exists).Select(Item).ToList();

    TreeViewItem Item(string path)
    {
        var isDir = Directory.Exists(path);
        var item = new TreeViewItem { Header = Path.GetFileName(path), Tag = path, IsExpanded = expanded.Contains(path) || Folders.Contains(Path.GetRelativePath(Workspace.Root, path)) };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property != TreeViewItem.IsExpandedProperty) return;
            if (item.IsExpanded) expanded.Add(path); else expanded.Remove(path);
        };
        if (isDir)
            item.ItemsSource = Directory.EnumerateDirectories(path).Where(d => Path.GetFileName(d) is not ("bin" or "obj")).Order()
                .Concat(Directory.EnumerateFiles(path).Order()).Select(Item).ToList();
        else if (ProjectFiles.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            ToolTip.SetTip(item, new Image { Source = TryBitmap(path), MaxWidth = 240, MaxHeight = 240 });
            // Drag an image onto the canvas to add an Image node showing it.
            PointerPressedEventArgs? pressed = null;
            item.AddHandler(PointerPressedEvent, (_, e) => pressed = e.GetCurrentPoint(item).Properties.IsLeftButtonPressed ? e : null, RoutingStrategies.Tunnel);
            item.AddHandler(PointerMovedEvent, async (_, e) =>
            {
                if (pressed is not { } start || Point.Distance(start.GetPosition(item), e.GetPosition(item)) < 6) return;
                pressed = null;
                var data = new DataTransfer();
                data.Add(DataTransferItem.Create(CanvasView.AssetFormat, Path.GetRelativePath(Workspace.Root, path).Replace('\\', '/')));
                await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy);
            }, RoutingStrategies.Tunnel);
            item.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel);
        }
        item.ContextFlyout = Menu(path, isDir);
        return item;
    }

    static Bitmap? TryBitmap(string path)
    {
        try { return new Bitmap(path); }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { return null; }
    }

    static string Area(string path) => Path.GetRelativePath(Workspace.Root, path).Split(Path.DirectorySeparatorChar)[0];

    System.Xml.Linq.XDocument? UiDoc(string path) =>
        Workspace.Project?.Screens.Values.Concat(Workspace.Project.Components.Values).FirstOrDefault(d => new Uri(d.BaseUri).LocalPath == path);

    void Open(string path)
    {
        if (Path.GetExtension(path) == ".cs") CodeView.Open(path);
        else if (UiDoc(path) is { } graph) CanvasView.ShowScreen((string)graph.Root!.Attribute("id")!); // screens and component masters
    }

    MenuFlyout? Menu(string path, bool isDir)
    {
        var items = new List<MenuItem>();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = L.T(header) };
            item.Click += (_, _) => action();
            items.Add(item);
        }
        var area = Area(path);
        var topLevel = Folders.Contains(Path.GetRelativePath(Workspace.Root, path));
        if (isDir && area == "UI") { Add("New Screen…", () => NewUi(screen: true)); Add("New Component…", () => NewUi(screen: false)); }
        if (isDir && area == "Source") Add("New C# Class…", () => NewClass(path));
        if (isDir && area is "Source" or "Assets") Add("New Folder…", () => NewFolder(path));
        if (!topLevel && area != "Bindings" && !(isDir && area == "UI")) { Add("Rename…", () => Rename(path, isDir)); Add("Delete", () => Delete(path, isDir)); }
        if (items.Count == 0) return null;
        var menu = new MenuFlyout();
        items.ForEach(i => menu.Items.Add(i));
        return menu;
    }

    Window Owner => (Window)TopLevel.GetTopLevel(this)!;

    async Task<string?> Ask(string title, string message, string initial = "")
    {
        var answer = await Dialogs.Prompt(Owner, L.T(title), L.T(message), initial);
        return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
    }

    async Task Fail(string title, string message) => await Dialogs.Info(Owner, L.T(title), new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

    /// <summary>Runs a screen/component file operation as one UI history step.</summary>
    async Task<bool> CommitUi(string label, Func<Dictionary<string, string?>> changes)
    {
        try
        {
            UiHistory.CommitFiles(label, changes());
            Workspace.Reload();
            return true;
        }
        catch (ArgumentException e) { await Fail(label, e.Message); return false; }
    }

    async void NewUi(bool screen)
    {
        if (Workspace.Project is not { } project || await Ask(screen ? "New Screen" : "New Component", "ID:", screen ? "" : "Comp.") is not { } id) return;
        if (await CommitUi(screen ? "New screen" : "New component", () => screen ? ProjectFiles.NewScreen(project, id) : ProjectFiles.NewComponent(project, id)) && screen)
            CanvasView.ShowScreen(id);
    }

    async void NewClass(string folder)
    {
        if (await Ask("New C# Class", "Class name:") is not { } name) return;
        var path = Path.Combine(folder, name + ".cs");
        try
        {
            if (File.Exists(path)) throw new ArgumentException($"{name}.cs already exists.");
            File.WriteAllText(path, ProjectFiles.ClassFile(Path.Combine(Workspace.Root, "Source"), Path.GetRelativePath(Path.Combine(Workspace.Root, "Source"), folder).Replace(".", ""), name));
            CodeView.Open(path);
        }
        catch (ArgumentException e) { await Fail("New C# Class", e.Message); }
    }

    async void NewFolder(string parent)
    {
        if (await Ask("New Folder", "Folder name:") is not { } name) return;
        if (!ValidFileName(name)) { await Fail("New Folder", $"'{name}' is not a valid folder name."); return; }
        Directory.CreateDirectory(Path.Combine(parent, name));
        expanded.Add(parent);
    }

    async void Rename(string path, bool isDir)
    {
        if (!isDir && UiDoc(path) is { } doc && Workspace.Project is { } project)
        {
            var oldId = (string)doc.Root!.Attribute("id")!;
            var screen = doc.Root.Name == "UIGraph";
            if (await Ask(screen ? "Rename Screen" : "Rename Component", "New ID (references follow):", oldId) is { } newId && newId != oldId)
                await CommitUi(screen ? "Rename screen" : "Rename component", () => screen ? ProjectFiles.RenameScreen(project, oldId, newId) : ProjectFiles.RenameComponent(project, oldId, newId));
            return;
        }
        if (Busy(path, isDir) is { } busy) { await Fail("Rename", busy); return; }
        var name = Path.GetFileName(path);
        if (await Ask("Rename", "New name:", name) is not { } newName || newName == name) return;
        var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (!ValidFileName(newName) || File.Exists(target) || Directory.Exists(target)) { await Fail("Rename", $"Can't rename to '{newName}'."); return; }
        if (isDir) Directory.Move(path, target); else File.Move(path, target);
        CodeView.Forget(path);
    }

    async void Delete(string path, bool isDir)
    {
        if (!isDir && UiDoc(path) is { } doc && Workspace.Project is { } project)
        {
            if (await Dialogs.Confirm(Owner, L.T("Delete"), L.F("Delete {0}? Bindings of its nodes are removed too. You can undo this with Ctrl+Z on the canvas.", Path.GetFileName(path)), L.T("Delete")))
                await CommitUi("Delete " + (doc.Root!.Name == "UIGraph" ? "screen" : "component"), () => ProjectFiles.Delete(project, doc));
            return;
        }
        if (Busy(path, isDir) is { } busy) { await Fail("Delete", busy); return; }
        if (!await Dialogs.Confirm(Owner, L.T("Delete"), L.F(isDir ? "Delete {0} and everything in it? This can't be undone." : "Delete {0}? This can't be undone.", Path.GetFileName(path)), L.T("Delete"))) return;
        if (isDir) Directory.Delete(path, recursive: true); else File.Delete(path);
        CodeView.Forget(path);
    }

    /// <summary>Unsaved code edits would be lost by renaming or deleting the file (or a folder containing it).</summary>
    static string? Busy(string path, bool isDir)
    {
        var files = isDir ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories) : [path];
        return files.FirstOrDefault(CodeView.IsDirty) is { } dirty ? $"{Path.GetFileName(dirty)} has unsaved changes. Save it first." : null;
    }

    static bool ValidFileName(string name) =>
        name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\');
}
