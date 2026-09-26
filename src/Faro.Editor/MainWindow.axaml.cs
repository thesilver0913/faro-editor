using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
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

    // File

    /// <summary>Restarts Faro on the chosen folder: the editor's state (buffers, language server) is per project.</summary>
    async void OpenProject(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Open Faro Project" });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;
        if (CodeView.AnyDirty && !await Dialogs.Confirm(this, "Unsaved changes", "Some code files have unsaved changes. Discard them and open the other project?", "Discard and Open"))
            return;
        var start = new ProcessStartInfo(Environment.ProcessPath!);
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location); // run via `dotnet Faro.Editor.dll`
        start.ArgumentList.Add(folder);
        Process.Start(start);
        closeConfirmed = true;
        Close();
    }

    void SaveAll(object? sender, RoutedEventArgs e) => CodeView.SaveAll();
    void Run(object? sender, RoutedEventArgs e) => Workspace.Run();
    void Exit(object? sender, RoutedEventArgs e) => Close();

    // Edit

    void SyncComponents(object? sender, RoutedEventArgs e) => CanvasView.SyncComponents();
    void Preferences(object? sender, RoutedEventArgs e) => new PreferencesWindow().ShowDialog(this);

    // Select

    void SelectAll(object? sender, RoutedEventArgs e) => CanvasView.Select(CanvasView.ScreenNodeIds);
    void SelectNone(object? sender, RoutedEventArgs e) => CanvasView.Select([]);
    void SelectBroken(object? sender, RoutedEventArgs e) => CanvasView.Select(Workspace.Issues.Select(i => i.NodeId).Intersect(CanvasView.ScreenNodeIds));

    async void SelectById(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.Prompt(this, "Select Node by ID", "Node ID on the current screen:") is not { Length: > 0 } id) return;
        if (CanvasView.ScreenNodeIds.Contains(id.Trim())) CanvasView.Select([id.Trim()]);
        else await Dialogs.Info(this, "Select Node by ID", new TextBlock { Text = $"No node '{id.Trim()}' on the current screen." });
    }

    // Window

    void ShowCanvas(object? sender, RoutedEventArgs e) => Activate("Canvas");
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
