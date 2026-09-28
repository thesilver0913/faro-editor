using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Faro.Editor;

public partial class MainWindow : Window
{
    bool closeConfirmed;

    /// <summary>Which history the undo/redo arrows and Ctrl+Z act on: the UI graph or the code (spec §10).</summary>
    bool codeHistory;

    public MainWindow()
    {
        InitializeComponent();
        // UI language (L): menu headers from the XAML, panel titles once the dock layout exists.
        foreach (var item in this.GetLogicalDescendants().OfType<MenuItem>())
            if (item.Header is string header) item.Header = L.T(header);
        Opened += (_, _) =>
        {
            if (Dock.Layout is null) return;
            foreach (var dockable in Dockables(Dock.Layout)) dockable.Title = L.T(dockable.Title);
        };
        // Follow the pane the user last clicked or focused: canvas → UI graph history, code/chat → code history.
        void Track(object? source)
        {
            var pane = (source as Visual)?.GetSelfAndVisualAncestors().FirstOrDefault(v => v is CanvasView or InspectorView or CodeView or ChatView);
            if (pane is null) return;
            codeHistory = pane is CodeView or ChatView;
            UpdateHistory();
        }
        AddHandler(PointerPressedEvent, (_, e) => Track(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, e) => Track(e.Source), RoutingStrategies.Bubble, handledEventsToo: true);
        UiHistory.Changed += UpdateHistory;
        CodeView.HistoryChanged += UpdateHistory;
        UpdateHistory();
        Closed += (_, _) => Workspace.Stop(); // the app started with Run goes with the editor
        RestoreLayout();
        TrustItem.IsVisible = !Workspace.Trusted;
        Closed += (_, _) => SaveLayout();
        // An untitled project or unsaved code would be lost on close: ask first.
        Closing += async (_, e) =>
        {
            if (closeConfirmed) return;
            e.Cancel = true;
            if (!await LeaveUntitled()) return;
            if (CodeView.AnyDirty && !await Dialogs.Confirm(this, L.T("Unsaved changes"), L.T("Some code files have unsaved changes. Close Faro and discard them?"), L.T("Discard and Close")))
                return;
            closeConfirmed = true;
            Close();
        };
    }

    // Layout (spec §14)

    static IEnumerable<Dock.Model.Core.IDockable> Dockables(Dock.Model.Core.IDockable dockable) =>
        dockable is Dock.Model.Core.IDock { VisibleDockables: { } children } ? children.SelectMany(Dockables).Prepend(dockable) : [dockable];

    Dictionary<string, double> defaultProportions = [];

    void ApplyProportions(Dictionary<string, double> proportions)
    {
        if (Dock.Layout is null) return;
        foreach (var dockable in Dockables(Dock.Layout))
            if (dockable.Id is { } id && proportions.TryGetValue(id, out var proportion)) dockable.Proportion = proportion;
    }

    Dictionary<string, double> Proportions() => Dock.Layout is null ? [] : Dockables(Dock.Layout)
        .Where(d => d.Id is not null && !double.IsNaN(d.Proportion)).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().Proportion);

    void ResetLayout(object? sender, RoutedEventArgs e) => ApplyProportions(defaultProportions);

    void RestoreLayout()
    {
        var settings = FaroSettings.Current;
        if (settings.WindowWidth > 0) (Width, Height) = (settings.WindowWidth, settings.WindowHeight);
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        Opened += (_, _) =>
        {
            defaultProportions = Proportions(); // as laid out in MainWindow.axaml
            ApplyProportions(settings.PaneProportions);
        };
    }

    void SaveLayout()
    {
        var settings = FaroSettings.Current;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) (settings.WindowWidth, settings.WindowHeight) = (Bounds.Width, Bounds.Height);
        settings.PaneProportions = Proportions();
        settings.Save();
    }

    // Undo / redo

    void UpdateHistory()
    {
        var (canUndo, canRedo) = codeHistory ? (CodeView.CanUndo, CodeView.CanRedo) : (UiHistory.UndoLabel is not null, UiHistory.RedoLabel is not null);
        UndoItem.IsEnabled = canUndo;
        RedoItem.IsEnabled = canRedo;
        UndoItem.Header = codeHistory || UiHistory.UndoLabel is null ? L.T("_Undo") : $"{L.T("_Undo")} {UiHistory.UndoLabel}";
        RedoItem.Header = codeHistory || UiHistory.RedoLabel is null ? "_Redo" : $"_Redo {UiHistory.RedoLabel}";
        HistoryLabel.Text = codeHistory ? L.F("History: Code — {0}", CodeView.HistoryFile ?? L.T("no file")) : L.T("History: UI graph");
    }

    async void Undo(object? sender, RoutedEventArgs e)
    {
        if (codeHistory) CodeView.Undo();
        else if (UiHistory.Undo() is { } error) await Dialogs.Info(this, L.T("Undo"), new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
        else Workspace.Reload();
        UpdateHistory();
    }

    async void Redo(object? sender, RoutedEventArgs e)
    {
        if (codeHistory) CodeView.Redo();
        else if (UiHistory.Redo() is { } error) await Dialogs.Info(this, L.T("Redo"), new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
        else Workspace.Reload();
        UpdateHistory();
    }

    /// <summary>Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) when no focused control handled them (text boxes and the code editor undo their own text).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        // Canvas editing keys, when the canvas is the pane in use (text boxes handle their own Delete/arrows first).
        if (!codeHistory && e.KeyModifiers == KeyModifiers.None && e.Key == Key.Delete) { CanvasView.DeleteSelection(); e.Handled = true; return; }
        if (!codeHistory && e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down) { CanvasView.MoveSelection(e.Key == Key.Up ? -1 : 1); e.Handled = true; return; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (!codeHistory && e.Key is Key.C or Key.V or Key.D or Key.X && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) // canvas nodes (text boxes handle their own)
        {
            if (e.Key == Key.C) CanvasView.CopySelection();
            else if (e.Key == Key.X) CanvasView.CutSelection();
            else if (e.Key == Key.V) CanvasView.PasteClipboard();
            else CanvasView.DuplicateSelection();
        }
        else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) SaveAs(this, e);
        else if (e.Key == Key.S) Save(this, e);
        else if (e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Undo(this, e);
        else if (e.Key == Key.Y || e.Key == Key.Z) Redo(this, e);
        else return;
        e.Handled = true;
    }

    // File

    /// <summary>Open Folder: like the welcome screen (initializing non-Faro folders), then restarts Faro on it.</summary>
    async void OpenProject(object? sender, RoutedEventArgs e) => await WelcomeWindow.OpenFolder(this, folder => Relaunch(folder));

    /// <summary>Leaves restricted mode: trusts the folder and reopens it with the language server, previews and Run.</summary>
    async void TrustProject(object? sender, RoutedEventArgs e)
    {
        if (!await Dialogs.Confirm(this, L.T("Trust Project"), L.F("Trust {0}? Faro will restore, build and run its code.", Workspace.Root), L.T("Trust"))) return;
        ProjectSetup.Trust(Workspace.Root);
        Relaunch(Workspace.Root);
    }

    /// <summary>Close Project: back to the welcome screen.</summary>
    void CloseProject(object? sender, RoutedEventArgs e) => Relaunch(null);

    /// <summary>The editor's state (buffers, language server, history) is per project, so switching restarts Faro.</summary>
    async void Relaunch(string? folder)
    {
        if (!await LeaveUntitled()) return;
        if (CodeView.AnyDirty && !await Dialogs.Confirm(this, L.T("Unsaved changes"), L.T("Some code files have unsaved changes. Discard them?"), L.T("Discard")))
            return;
        var start = new ProcessStartInfo(Environment.ProcessPath!);
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location); // run via `dotnet Faro.Editor.dll`
        if (folder is not null) start.ArgumentList.Add(folder);
        Process.Start(start);
        closeConfirmed = true;
        Close();
    }

    /// <summary>Save: the code files (canvas edits are written as they happen); an untitled project first gets a name and a place.</summary>
    async void Save(object? sender, RoutedEventArgs e)
    {
        if (ProjectSetup.IsUntitled(Workspace.Root)) SaveAs(sender, e);
        else await CodeView.SaveAll(this);
    }

    /// <summary>Save As: the project goes to a new name and place (copied), and Faro reopens it there.</summary>
    async void SaveAs(object? sender, RoutedEventArgs e)
    {
        if (await SaveProjectAs() is { } saved) { savedAs = true; Relaunch(saved); }
    }

    bool savedAs;

    async Task<string?> SaveProjectAs()
    {
        var untitled = ProjectSetup.IsUntitled(Workspace.Root);
        var name = new TextBox { Text = untitled ? "MyFaroApp" : Path.GetFileName(Workspace.Root) + "Copy" };
        var location = new TextBox { Text = untitled ? ProjectSetup.DefaultLocation : Path.GetDirectoryName(Workspace.Root), MinWidth = 380 };
        var browse = new Button { Content = L.T("Browse…") };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = L.T("Project Location") });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) location.Text = path;
        };
        DockPanel.SetDock(browse, Avalonia.Controls.Dock.Right);
        var form = new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = L.T("Name") }, name, new TextBlock { Text = L.T("Location") }, new DockPanel { Children = { browse, location } } },
        };
        while (await Dialogs.Form(this, L.T("Save Project As"), form, L.T("Save")))
        {
            await CodeView.SaveAll(this);
            try { return ProjectSetup.SaveAs(Workspace.Root, location.Text ?? "", (name.Text ?? "").Trim()); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                await Dialogs.Info(this, L.T("Save Project As"), new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
            }
        }
        return null;
    }

    /// <summary>Leaving an untitled project: save it somewhere, or discard it. False: stay.</summary>
    async Task<bool> LeaveUntitled()
    {
        if (savedAs || !ProjectSetup.IsUntitled(Workspace.Root)) return true;
        switch (await Dialogs.Choose(this, L.T("Save project?"), L.F("{0} hasn't been saved yet. Save it before leaving?", Path.GetFileName(Workspace.Root)), [L.T("Save As…"), L.T("Don't Save")]))
        {
            case 0: return savedAs = await SaveProjectAs() is not null;
            case 1: ProjectSetup.Discard(Workspace.Root); return true;
            default: return false;
        }
    }
    void Run(object? sender, RoutedEventArgs e) { if (!Workspace.Running) ConsoleView.RunOrStop(); }
    void Stop(object? sender, RoutedEventArgs e) => Workspace.Stop();
    void Exit(object? sender, RoutedEventArgs e) => Close();

    // Edit

    void Delete(object? sender, RoutedEventArgs e) => CanvasView.DeleteSelection();
    void CutNodes(object? sender, RoutedEventArgs e) => CanvasView.CutSelection();
    void CopyNodes(object? sender, RoutedEventArgs e) => CanvasView.CopySelection();
    void PasteNodes(object? sender, RoutedEventArgs e) => CanvasView.PasteClipboard();
    void DuplicateNodes(object? sender, RoutedEventArgs e) => CanvasView.DuplicateSelection();
    void SyncComponents(object? sender, RoutedEventArgs e) => CanvasView.SyncComponents();
    void Preferences(object? sender, RoutedEventArgs e) => new PreferencesWindow().ShowDialog(this);

    // Select

    void SelectAll(object? sender, RoutedEventArgs e) => CanvasView.Select(CanvasView.ScreenNodeIds);
    void SelectNone(object? sender, RoutedEventArgs e) => CanvasView.Select([]);
    void SelectBroken(object? sender, RoutedEventArgs e) => CanvasView.Select(Workspace.Issues.Where(i => i.Screen == CanvasView.CurrentScreen).Select(i => i.NodeId).Intersect(CanvasView.ScreenNodeIds));

    async void SelectById(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.Prompt(this, L.T("Select Node by ID"), L.T("Node ID on the current screen:")) is not { Length: > 0 } id) return;
        if (CanvasView.ScreenNodeIds.Contains(id.Trim())) CanvasView.Select([id.Trim()]);
        else await Dialogs.Info(this, L.T("Select Node by ID"), new TextBlock { Text = L.F("No node '{0}' on the current screen.", id.Trim()) });
    }

    // Window

    void ShowExplorer(object? sender, RoutedEventArgs e) => Activate("Explorer");
    void ShowProblems(object? sender, RoutedEventArgs e) { Activate("Console"); ConsoleView.Show(ConsoleView.Tab.Problems); }
    void ShowCanvas(object? sender, RoutedEventArgs e) => Activate("Canvas");
    void ShowInspector(object? sender, RoutedEventArgs e) => Activate("Inspector");
    void ShowCode(object? sender, RoutedEventArgs e) => Activate("Code");
    void ShowChat(object? sender, RoutedEventArgs e) { Activate("Console"); ConsoleView.Show(ConsoleView.Tab.VibeCoding); }
    void ShowOutput(object? sender, RoutedEventArgs e) { Activate("Console"); ConsoleView.Show(ConsoleView.Tab.Output); }
    void ToggleFullScreen(object? sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    void Activate(string id)
    {
        if (Dock.Factory is { } factory && Dock.Layout is { } layout && factory.FindDockable(layout, d => d.Id == id) is { } dockable)
        {
            factory.SetActiveDockable(dockable);
            if (dockable.Owner is Dock.Model.Core.IDock owner) factory.SetFocusedDockable(owner, dockable);
        }
    }

    // Help

    void About(object? sender, RoutedEventArgs e) => Dialogs.Info(this, L.T("About Faro"), new StackPanel
    {
        Spacing = 8,
        Children =
        {
            new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png"))), Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left },
            new TextBlock { Text = $"Faro {App.Version}", FontSize = 20, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = L.T("Figma × UI Binding × Vibe Coding — a visual UI editor prototype.\n“Faro” is Italian for lighthouse."), TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = L.T("MIT License. Third-party components: see THIRD-PARTY-NOTICES.md."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
        },
    });
}
