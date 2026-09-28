using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Faro.Editor;

/// <summary>
/// VS Code-style file explorer over the whole project folder (spec §4). Click opens: screens and components on the
/// canvas, C# and other text files in the code editor. Right-click / keys: new file or folder, cut / copy / paste,
/// rename (F2), delete (Del), copy path, reveal in the OS file manager; drag to move, drop files from the OS to copy.
/// Screen/component operations follow their references and are UI history steps; UI/ and Bindings/ files belong to
/// those, so they aren't moved or pasted as plain files.
/// </summary>
public sealed class ExplorerView : UserControl
{
    static readonly string[] Folders = ["UI", "Source", "Bindings", "Assets"];
    static readonly string[] Hidden = ["bin", "obj", ".git", ".vs"];
    static readonly HashSet<string> expanded = [];
    static string? openedRoot; // the Faro folders start expanded once per project; after that the user decides
    static readonly DataFormat<string> PathFormat = DataFormat.CreateInProcessFormat<string>("faro-path");

    /// <summary>Files cut or copied in the explorer (paths), pasted into a folder.</summary>
    static (List<string> Paths, bool Cut)? clipboard;

    readonly TreeView tree = new();
    readonly DispatcherTimer rebuild = new() { Interval = TimeSpan.FromMilliseconds(300) };
    FileSystemWatcher? watcher;

    public ExplorerView()
    {
        rebuild.Tick += (_, _) => { rebuild.Stop(); Build(); };
        tree.SelectionChanged += (_, _) => { if (Selected is { } path) Open(path); };
        tree.ContextRequested += (_, e) => ShowMenu(null, e); // empty area: the project folder itself
        tree.AddHandler(KeyDownEvent, OnTreeKey, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(tree, true);
        tree.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DropFolder(e) is null ? DragDropEffects.None
            : e.DataTransfer.Contains(PathFormat) ? DragDropEffects.Move : DragDropEffects.Copy);
        tree.AddHandler(DragDrop.DropEvent, OnDrop);

        // Header buttons, as in VS Code's explorer title bar.
        var bar = Icons.Toolbar(new WrapPanel
        {
            ItemSpacing = 4, LineSpacing = 4, Margin = new(4),
            Children =
            {
                Icons.Button(FluentAvalonia.UI.Controls.FASymbol.Add, "New File…", () => NewFile(TargetFolder())),
                Icons.Button(FluentAvalonia.UI.Controls.FASymbol.Folder, "New Folder…", () => NewFolder(TargetFolder())),
                Icons.Button(FluentAvalonia.UI.Controls.FASymbol.Refresh, "Refresh", Build),
                Icons.Button(FluentAvalonia.UI.Controls.FASymbol.ChevronUp, "Collapse All", () => { expanded.Clear(); Build(); }),
            },
        });
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { bar, tree } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Build();
        if (!Directory.Exists(Workspace.Root)) return;
        watcher = new FileSystemWatcher(Workspace.Root) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        FileSystemEventHandler changed = (_, a) =>
        {
            if (!IsHidden(a.FullPath)) Dispatcher.UIThread.Post(() => { rebuild.Stop(); rebuild.Start(); }); // debounce bursts of events
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

    static bool IsHidden(string path) => Path.GetRelativePath(Workspace.Root, path).Split(Path.DirectorySeparatorChar).Any(HiddenName);

    /// <summary>Build outputs and tool folders: bin/obj (C#), target (Java/Maven), .git, .vs.</summary>
    static bool HiddenName(string name) => Hidden.Contains(name) || Workspace.IsJava && name == "target";

    static IEnumerable<string> Children(string dir) =>
        Directory.EnumerateDirectories(dir).Where(d => !HiddenName(Path.GetFileName(d))).Order()
            .Concat(Directory.EnumerateFiles(dir).Order());

    void Build()
    {
        if (openedRoot != Workspace.Root)
        {
            openedRoot = Workspace.Root;
            expanded.UnionWith(Folders.Select(f => Path.Combine(Workspace.Root, f)));
        }
        tree.ItemsSource = Directory.Exists(Workspace.Root) ? Children(Workspace.Root).Select(Item).ToList() : [];
    }

    string? Selected => tree.SelectedItem is TreeViewItem { Tag: string path } ? path : null;

    /// <summary>Where new files go: the selected folder, the selected file's folder, or the project folder.</summary>
    string TargetFolder() => Selected is { } path ? Directory.Exists(path) ? path : Path.GetDirectoryName(path)! : Workspace.Root;

    TreeViewItem Item(string path)
    {
        var isDir = Directory.Exists(path);
        var item = new TreeViewItem { Header = Path.GetFileName(path), Tag = path, IsExpanded = expanded.Contains(path) };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property != TreeViewItem.IsExpandedProperty) return;
            if (item.IsExpanded) expanded.Add(path); else expanded.Remove(path);
        };
        if (isDir) item.ItemsSource = Children(path).Select(Item).ToList();
        else if (IsImage(path)) ToolTip.SetTip(item, new Image { Source = TryBitmap(path), MaxWidth = 240, MaxHeight = 240 });
        item.ContextRequested += (_, e) => ShowMenu(path, e);

        // Drag: move within the explorer; an image can also be dropped on the canvas (an Image node).
        PointerPressedEventArgs? pressed = null;
        item.AddHandler(PointerPressedEvent, (_, e) => pressed = e.GetCurrentPoint(item).Properties.IsLeftButtonPressed && e.Source is Visual v && v.FindAncestorOfType<TreeViewItem>(includeSelf: true) == item ? e : null, RoutingStrategies.Tunnel);
        item.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pressed is not { } start || Point.Distance(start.GetPosition(item), e.GetPosition(item)) < 6) return;
            pressed = null;
            var data = new DataTransfer();
            var entry = DataTransferItem.Create(PathFormat, path);
            if (IsImage(path)) entry.Set(CanvasView.AssetFormat, Relative(path).Replace('\\', '/'));
            data.Add(entry);
            await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move | DragDropEffects.Copy);
        }, RoutingStrategies.Tunnel);
        item.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel);
        return item;
    }

    static bool IsImage(string path) => ProjectFiles.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Text, not binary: no NUL byte in the first 8 KB.</summary>
    static bool IsText(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[8192];
            return Array.IndexOf(buffer, (byte)0, 0, stream.Read(buffer)) < 0;
        }
        catch (IOException) { return false; }
    }

    static Bitmap? TryBitmap(string path)
    {
        try { return new Bitmap(path); }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { return null; }
    }

    static string Relative(string path) => Path.GetRelativePath(Workspace.Root, path);

    static string Area(string path) => Relative(path).Split(Path.DirectorySeparatorChar)[0];

    System.Xml.Linq.XDocument? UiDoc(string path) =>
        Workspace.Project?.Screens.Values.Concat(Workspace.Project.Components.Values).FirstOrDefault(d => new Uri(d.BaseUri).LocalPath == path);

    void Open(string path)
    {
        if (Directory.Exists(path)) return;
        if (UiDoc(path) is { } graph) CanvasView.ShowScreen((string)graph.Root!.Attribute("id")!); // screens and component masters
        else if (!IsImage(path) && IsText(path)) CodeView.Open(path); // C# and any other text file
    }

    /// <summary>Plain file operations (cut, copy, paste, move) stay out of UI/ and Bindings/: those files belong to screens.</summary>
    static bool Plain(string path) => Area(path) is not ("UI" or "Bindings") && !Folders.Contains(Relative(path)) && path != Workspace.Root;

    static bool PlainFolder(string dir) => Area(dir) is not ("UI" or "Bindings") || dir == Workspace.Root;

    /// <summary>The context menu of an item, or of the project folder (<paramref name="path"/> null), built when it opens.</summary>
    void ShowMenu(string? path, ContextRequestedEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        var menu = new MenuFlyout();
        {
            var target = path ?? Workspace.Root;
            var isDir = Directory.Exists(target);
            var folder = isDir ? target : Path.GetDirectoryName(target)!;
            void Add(string header, Action action, string? gesture = null, bool enabled = true)
            {
                var item = new MenuItem { Header = L.T(header), IsEnabled = enabled, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            void Separator() { if (menu.Items.Count > 0 && menu.Items[^1] is not Avalonia.Controls.Separator) menu.Items.Add(new Separator()); }

            if (isDir)
            {
                if (PlainFolder(target)) { Add("New File…", () => NewFile(target)); Add("New Folder…", () => NewFolder(target)); }
                if (Area(target) == "UI" && path is not null) { Add("New Screen…", () => NewUi(screen: true)); Add("New Component…", () => NewUi(screen: false)); }
                if (Area(target) == "Source" && path is not null) Add(Workspace.IsJava ? "New Java Class…" : "New C# Class…", () => NewClass(target));
            }
            else if (UiDoc(target) is not null || Path.GetExtension(target) == ".xml") Add("Open as Text", () => CodeView.Open(target));
            Separator();
            if (path is not null && Plain(target)) { Add("Cut", () => clipboard = ([target], true), "Ctrl+X"); Add("Copy", () => clipboard = ([target], false), "Ctrl+C"); }
            if (clipboard is not null && PlainFolder(folder)) Add("Paste", () => Paste(folder), "Ctrl+V");
            Separator();
            Add("Copy Path", () => CopyText(target));
            Add("Copy Relative Path", () => CopyText(Relative(target)));
            Add("Reveal in File Manager", () => Reveal(target));
            if (path is not null && Renamable(target))
            {
                Separator();
                Add("Rename…", () => Rename(target), "F2");
                Add("Delete", () => Delete(target), "Delete");
            }
        }
        menu.ShowAt(this, showAtPointer: true);
    }

    /// <summary>Not the project folder, the four spec folders, or a Bindings file (those follow their screen).</summary>
    static bool Renamable(string path) => path != Workspace.Root && !Folders.Contains(Relative(path)) && Area(path) != "Bindings";

    void OnTreeKey(object? sender, KeyEventArgs e)
    {
        if (Selected is not { } path) return;
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.F2 && Renamable(path)) Rename(path);
        else if (e.Key == Key.Delete && Renamable(path)) Delete(path);
        else if (e.Key == Key.Enter) Open(path);
        else if (command && e.Key is Key.C or Key.X && Plain(path)) clipboard = ([path], e.Key == Key.X);
        else if (command && e.Key == Key.V && clipboard is not null && PlainFolder(TargetFolder())) Paste(TargetFolder());
        else return;
        e.Handled = true; // not the canvas's Ctrl+C / Delete
    }

    Window Owner => (Window)TopLevel.GetTopLevel(this)!;

    async void CopyText(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } system) await system.SetTextAsync(text);
    }

    /// <summary>Shows the file in Explorer / Finder (selected), or opens its folder on Linux.</summary>
    static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS()) Process.Start("open", ["-R", path]);
            else Process.Start("xdg-open", [Directory.Exists(path) ? path : Path.GetDirectoryName(path)!]);
        }
        catch (System.ComponentModel.Win32Exception) { } // no file manager available
    }

    /// <summary>"name.ext", or "name copy.ext", "name copy 2.ext"… when taken.</summary>
    static string FreeName(string folder, string name)
    {
        var (stem, ext) = (Path.GetFileNameWithoutExtension(name), Path.GetExtension(name));
        return Enumerable.Range(0, int.MaxValue).Select(i => Path.Combine(folder, i switch { 0 => name, 1 => $"{stem} copy{ext}", _ => $"{stem} copy {i}{ext}" }))
            .First(p => !File.Exists(p) && !Directory.Exists(p));
    }

    async void Paste(string folder)
    {
        if (clipboard is not { } clip) return;
        foreach (var source in clip.Paths.Where(p => File.Exists(p) || Directory.Exists(p)))
        {
            if (Directory.Exists(source) && (folder + Path.DirectorySeparatorChar).StartsWith(source + Path.DirectorySeparatorChar)) continue; // not into itself
            if (clip.Cut && Busy(source, Directory.Exists(source)) is { } busy) { await Fail("Paste", busy); continue; }
            var target = FreeName(folder, Path.GetFileName(source));
            if (clip.Cut) Move(source, target);
            else if (Directory.Exists(source)) CopyFolder(source, target);
            else File.Copy(source, target);
        }
        if (clip.Cut) clipboard = null;
        expanded.Add(folder);
    }

    static void Move(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
        CodeView.Forget(source);
    }

    static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    /// <summary>The folder a drag is over: the item's folder (a file's parent), or the project folder on empty space.</summary>
    string? DropFolder(DragEventArgs e)
    {
        var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        var folder = item?.Tag is string path ? Directory.Exists(path) ? path : Path.GetDirectoryName(path)! : Workspace.Root;
        return PlainFolder(folder) ? folder : null;
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DropFolder(e) is not { } folder) return;
        if (e.DataTransfer.TryGetValue(PathFormat) is { } source) // moved inside the explorer
        {
            if (!Plain(source) || Path.GetDirectoryName(source) == folder || (folder + Path.DirectorySeparatorChar).StartsWith(source + Path.DirectorySeparatorChar)) return;
            if (Busy(source, Directory.Exists(source)) is { } busy) { await Fail("Move", busy); return; }
            Move(source, FreeName(folder, Path.GetFileName(source)));
        }
        else if (e.DataTransfer.TryGetFiles() is { } files) // dropped from the OS file manager: copied in
            foreach (var local in files.Select(f => f.TryGetLocalPath()).OfType<string>())
            {
                var target = FreeName(folder, Path.GetFileName(local));
                if (Directory.Exists(local)) CopyFolder(local, target); else if (File.Exists(local)) File.Copy(local, target);
            }
        expanded.Add(folder);
    }

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
        var title = Workspace.IsJava ? "New Java Class" : "New C# Class";
        if (await Ask(title, "Class name:") is not { } name) return;
        var path = Path.Combine(folder, name + (Workspace.IsJava ? ".java" : ".cs"));
        var relative = Path.GetRelativePath(Path.Combine(Workspace.Root, "Source"), folder).Replace(".", "");
        try
        {
            if (File.Exists(path)) throw new ArgumentException($"{Path.GetFileName(path)} already exists.");
            File.WriteAllText(path, Workspace.IsJava ? JavaProject.ClassFile(relative, name) : ProjectFiles.ClassFile(Path.Combine(Workspace.Root, "Source"), relative, name));
            CodeView.Open(path);
        }
        catch (ArgumentException e) { await Fail(title, e.Message); }
    }

    async void NewFile(string folder)
    {
        if (!PlainFolder(folder)) folder = Workspace.Root;
        if (await Ask("New File", "File name:") is not { } name) return;
        var path = Path.Combine(folder, name);
        if (!ValidFileName(name) || File.Exists(path) || Directory.Exists(path)) { await Fail("New File", $"Can't create '{name}'."); return; }
        File.WriteAllText(path, "");
        expanded.Add(folder);
        CodeView.Open(path);
    }

    async void NewFolder(string parent)
    {
        if (!PlainFolder(parent)) parent = Workspace.Root;
        if (await Ask("New Folder", "Folder name:") is not { } name) return;
        if (!ValidFileName(name)) { await Fail("New Folder", $"'{name}' is not a valid folder name."); return; }
        Directory.CreateDirectory(Path.Combine(parent, name));
        expanded.Add(parent);
    }

    async void Rename(string path)
    {
        var isDir = Directory.Exists(path);
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
        Move(path, target);
    }

    async void Delete(string path)
    {
        var isDir = Directory.Exists(path);
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

    /// <summary>Unsaved code edits would be lost by moving or deleting the file (or a folder containing it).</summary>
    static string? Busy(string path, bool isDir)
    {
        var files = isDir ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : [path];
        return files.FirstOrDefault(CodeView.IsDirty) is { } dirty ? L.F("{0} has unsaved changes. Save it first.", Path.GetFileName(dirty)) : null;
    }

    static bool ValidFileName(string name) =>
        name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\');
}
