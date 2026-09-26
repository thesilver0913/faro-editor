using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Faro.Editor;

/// <summary>
/// Start screen (GIMP / Adobe style): recent projects, open a folder (initializing it as a Faro project
/// if needed), or create a new project in Documents/Faro (or a chosen location) from a template.
/// </summary>
public sealed class WelcomeWindow : Window
{
    /// <summary>Raised with the project folder to open.</summary>
    public event Action<string>? ProjectChosen;

    readonly ContentControl right = new();

    public WelcomeWindow()
    {
        Title = "Welcome to Faro";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png")));
        Width = 820;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var create = new Button { Content = "New Project…", HorizontalAlignment = HorizontalAlignment.Stretch };
        create.Click += (_, _) => right.Content = NewProjectForm();
        var open = new Button { Content = "Open Folder…", HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += async (_, _) => await OpenFolder(this, dir => ProjectChosen?.Invoke(dir));
        var left = new StackPanel
        {
            Width = 240, Spacing = 10, Margin = new(28),
            Children =
            {
                new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png"))), Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Left },
                new TextBlock { Text = "Faro", FontSize = 30, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3), Opacity = 0.6, Margin = new(0, -8, 0, 16) },
                create,
                open,
            },
        };
        right.Content = Recent();
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);
        Content = new DockPanel { Children = { left, new Border { Padding = new(28), Child = right } } };
    }

    Control Recent()
    {
        var list = new StackPanel { Spacing = 4 };
        foreach (var dir in FaroSettings.Current.RecentProjects.Where(Directory.Exists))
        {
            var item = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel { Children = { new TextBlock { Text = Path.GetFileName(dir), FontWeight = FontWeight.SemiBold }, new TextBlock { Text = dir, Opacity = 0.6, FontSize = 12 } } },
            };
            item.Click += (_, _) => ProjectChosen?.Invoke(dir);
            list.Children.Add(item);
        }
        if (list.Children.Count == 0)
            list.Children.Add(new TextBlock { Text = "No recent projects yet. Create a new project or open a folder.", Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        return new DockPanel
        {
            Children =
            {
                Header("Recent"),
                new ScrollViewer { Content = list },
            },
        };
    }

    Control NewProjectForm()
    {
        var name = new TextBox { Text = "MyFaroApp" };
        var location = new TextBox { Text = ProjectSetup.DefaultLocation };
        var browse = new Button { Content = "Browse…" };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Project Location" });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) location.Text = path;
        };
        var template = new ComboBox { ItemsSource = ProjectSetup.Templates, SelectedIndex = 0, MinWidth = 160 };
        // Spec §2: the language is chosen when a project is created and can't change later; the prototype supports C# only.
        var language = new ComboBox { ItemsSource = new[] { "C#" }, SelectedIndex = 0, IsEnabled = false, MinWidth = 160 };
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        var create = new Button { Content = "Create", IsDefault = true };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => right.Content = Recent();
        create.Click += (_, _) =>
        {
            try { ProjectChosen?.Invoke(ProjectSetup.Create(location.Text ?? "", (name.Text ?? "").Trim(), (string)template.SelectedItem!)); }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException) { error.Text = e.Message; }
        };
        DockPanel.SetDock(browse, Avalonia.Controls.Dock.Right);
        var form = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Label("Name"), name,
                Label("Location"), new DockPanel { Children = { browse, location } },
                Label("Template"), template,
                Label("Language"), language,
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { create, cancel } },
            },
        };
        return new DockPanel { Children = { Header("New Project"), new ScrollViewer { Content = form } } };
    }

    /// <summary>Picks a folder; one without faro.json is initialized after confirmation (existing files are never changed).</summary>
    public static async Task OpenFolder(Window owner, Action<string> open)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new() { Title = "Open Folder" });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir) return;
        if (!ProjectSetup.IsFaroProject(dir))
        {
            if (!await Dialogs.Confirm(owner, "Open Folder", $"{dir} isn't a Faro project yet. Initialize it? Missing folders and files are added; existing files aren't changed.", "Initialize"))
                return;
            try { ProjectSetup.Initialize(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                await Dialogs.Info(owner, "Open Folder", new TextBlock { Text = e.Message, TextWrapping = TextWrapping.Wrap });
                return;
            }
        }
        open(dir);
    }

    static TextBlock Header(string text)
    {
        var header = new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 12) };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        return header;
    }

    static TextBlock Label(string text) => new() { Text = text, Opacity = 0.8 };
}
