using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        // Unsaved code edits would be lost on close: ask first.
        Closing += async (_, e) =>
        {
            if (closeConfirmed || !CodeView.AnyDirty) return;
            e.Cancel = true;
            if (await Dialogs.Confirm(this, "Unsaved changes", "Some code files have unsaved changes. Close Faro and discard them?", "Discard and Close"))
            {
                closeConfirmed = true;
                Close();
            }
        };
    }

    // Undo / redo

    void UpdateHistory()
    {
        var (canUndo, canRedo) = codeHistory ? (CodeView.CanUndo, CodeView.CanRedo) : (UiHistory.UndoLabel is not null, UiHistory.RedoLabel is not null);
        UndoItem.IsEnabled = canUndo;
        RedoItem.IsEnabled = canRedo;
        UndoItem.Header = codeHistory || UiHistory.UndoLabel is null ? "_Undo" : $"_Undo {UiHistory.UndoLabel}";
        RedoItem.Header = codeHistory || UiHistory.RedoLabel is null ? "_Redo" : $"_Redo {UiHistory.RedoLabel}";
        HistoryLabel.Text = codeHistory ? $"History: Code — {CodeView.HistoryFile ?? "no file"}" : "History: UI graph";
    }

    async void Undo(object? sender, RoutedEventArgs e)
    {
        if (codeHistory) CodeView.Undo();
        else if (UiHistory.Undo() is { } error) await Dialogs.Info(this, "Undo", new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
        else Workspace.Reload();
        UpdateHistory();
    }

    async void Redo(object? sender, RoutedEventArgs e)
    {
        if (codeHistory) CodeView.Redo();
        else if (UiHistory.Redo() is { } error) await Dialogs.Info(this, "Redo", new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
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
        if (e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Undo(this, e);
        else if (e.Key == Key.Y || e.Key == Key.Z) Redo(this, e);
        else return;
        e.Handled = true;
    }

    // File

    /// <summary>Open Folder: like the welcome screen (initializing non-Faro folders), then restarts Faro on it.</summary>
    async void OpenProject(object? sender, RoutedEventArgs e) => await WelcomeWindow.OpenFolder(this, folder => Relaunch(folder));

    /// <summary>Close Project: back to the welcome screen.</summary>
    void CloseProject(object? sender, RoutedEventArgs e) => Relaunch(null);

    /// <summary>The editor's state (buffers, language server, history) is per project, so switching restarts Faro.</summary>
    async void Relaunch(string? folder)
    {
        if (CodeView.AnyDirty && !await Dialogs.Confirm(this, "Unsaved changes", "Some code files have unsaved changes. Discard them?", "Discard"))
            return;
        var start = new ProcessStartInfo(Environment.ProcessPath!);
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location); // run via `dotnet Faro.Editor.dll`
        if (folder is not null) start.ArgumentList.Add(folder);
        Process.Start(start);
        closeConfirmed = true;
        Close();
    }

    void SaveAll(object? sender, RoutedEventArgs e) => CodeView.SaveAll();
    void Run(object? sender, RoutedEventArgs e) => Workspace.Run();
    void Exit(object? sender, RoutedEventArgs e) => Close();

    // Edit

    void Delete(object? sender, RoutedEventArgs e) => CanvasView.DeleteSelection();
    void SyncComponents(object? sender, RoutedEventArgs e) => CanvasView.SyncComponents();
    void Preferences(object? sender, RoutedEventArgs e) => new PreferencesWindow().ShowDialog(this);

    // Select

    void SelectAll(object? sender, RoutedEventArgs e) => CanvasView.Select(CanvasView.ScreenNodeIds);
    void SelectNone(object? sender, RoutedEventArgs e) => CanvasView.Select([]);
    void SelectBroken(object? sender, RoutedEventArgs e) => CanvasView.Select(Workspace.Issues.Where(i => i.Screen == CanvasView.CurrentScreen).Select(i => i.NodeId).Intersect(CanvasView.ScreenNodeIds));

    async void SelectById(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.Prompt(this, "Select Node by ID", "Node ID on the current screen:") is not { Length: > 0 } id) return;
        if (CanvasView.ScreenNodeIds.Contains(id.Trim())) CanvasView.Select([id.Trim()]);
        else await Dialogs.Info(this, "Select Node by ID", new TextBlock { Text = $"No node '{id.Trim()}' on the current screen." });
    }

    // Window

    void ShowExplorer(object? sender, RoutedEventArgs e) => Activate("Explorer");
    void ShowCanvas(object? sender, RoutedEventArgs e) => Activate("Canvas");
    void ShowInspector(object? sender, RoutedEventArgs e) => Activate("Inspector");
    void ShowCode(object? sender, RoutedEventArgs e) => Activate("Code");
    void ShowChat(object? sender, RoutedEventArgs e) => Activate("Chat");
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

    void About(object? sender, RoutedEventArgs e) => Dialogs.Info(this, "About Faro", new StackPanel
    {
        Spacing = 8,
        Children =
        {
            new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png"))), Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left },
            new TextBlock { Text = $"Faro {Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3)}", FontSize = 20, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "Figma × UI Binding × Vibe Coding — a visual UI editor prototype.\n“Faro” is Italian for lighthouse.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "MIT License. Third-party components: see THIRD-PARTY-NOTICES.md.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
        },
    });
}
