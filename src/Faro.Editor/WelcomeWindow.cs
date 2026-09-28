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
/// if needed), or create a new project from a template. A new project starts untitled; File > Save As gives it
/// a name and a place (Documents/Faro by default).
/// </summary>
public sealed class WelcomeWindow : Window
{
    /// <summary>Raised with the project folder to open.</summary>
    public event Action<string>? ProjectChosen;

    readonly ContentControl right = new();

    public WelcomeWindow()
    {
        Title = L.T("Welcome to Faro");
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png")));
        Width = 820;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var create = new Button { Content = Icons.Label(FluentAvalonia.UI.Controls.FASymbol.Add, L.T("New Project…")), HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "accent" } };
        create.Click += (_, _) => right.Content = NewProjectForm();
        var open = new Button { Content = Icons.Label(FluentAvalonia.UI.Controls.FASymbol.OpenFolder, L.T("Open Folder…")), HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += async (_, _) => await OpenFolder(this, dir => ProjectChosen?.Invoke(dir));
        var left = new StackPanel
        {
            Width = 240, Spacing = 10, Margin = new(28),
            Children =
            {
                new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png"))), Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Left },
                new TextBlock { Text = L.T("Faro"), FontSize = 30, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = App.Version, Opacity = 0.6, Margin = new(0, -8, 0, 16) },
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
            var language = JavaProject.Is(dir) ? "Java" : "C#";
            var item = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Path.GetFileName(dir), FontWeight = FontWeight.SemiBold }, new TextBlock { Text = language, Opacity = 0.5, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } } },
                        new TextBlock { Text = dir, Opacity = 0.6, FontSize = 12, TextTrimming = TextTrimming.PathSegmentEllipsis },
                    },
                },
            };
            item.Click += (_, _) => ProjectChosen?.Invoke(dir);
            // Remove from the list (the folder itself stays).
            var forget = Icons.Button(FluentAvalonia.UI.Controls.FASymbol.Dismiss, "Remove from Recent", () =>
            {
                FaroSettings.Current.RecentProjects.Remove(dir);
                FaroSettings.Current.Save();
                right.Content = Recent();
            });
            forget.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(forget, Avalonia.Controls.Dock.Right);
            list.Children.Add(new DockPanel { Children = { forget, item } });
        }
        // Unsaved projects from an earlier session (a crash, or closed without deciding): reopen or discard.
        foreach (var dir in ProjectSetup.LeftoverUntitled())
        {
            var open = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel { Children = { new TextBlock { Text = Path.GetFileName(dir) + L.T(" (not saved)"), FontWeight = FontWeight.SemiBold }, new TextBlock { Text = L.T("Unsaved project from an earlier session"), Opacity = 0.6, FontSize = 12 } } },
            };
            open.Click += (_, _) => ProjectChosen?.Invoke(dir);
            var discard = new Button { Content = L.T("Discard"), VerticalAlignment = VerticalAlignment.Center };
            discard.Click += (_, _) => { ProjectSetup.Discard(dir); ProjectSetup.DeletePending(); right.Content = Recent(); };
            DockPanel.SetDock(discard, Avalonia.Controls.Dock.Right);
            list.Children.Add(new DockPanel { Children = { discard, open } });
        }
        if (list.Children.Count == 0)
            list.Children.Add(new TextBlock { Text = L.T("No recent projects yet. Create a new project or open a folder."), Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        return new DockPanel
        {
            Children =
            {
                Header(L.T("Recent")),
                new ScrollViewer { Content = list },
            },
        };
    }

    Control NewProjectForm()
    {
        var template = new ComboBox { ItemsSource = ProjectSetup.Templates, SelectedIndex = 0, MinWidth = 160 };
        // Spec §2: the language is chosen when a project is created and can't change later.
        var language = new ComboBox { ItemsSource = new[] { "C# (Avalonia)", "Java (JavaFX)" }, SelectedIndex = 0, MinWidth = 160 };
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        var create = new Button { Content = L.T("Create"), IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Cancel") };
        cancel.Click += (_, _) => right.Content = Recent();
        create.Click += (_, _) =>
        {
            try { ProjectChosen?.Invoke(ProjectSetup.CreateUntitled((string)template.SelectedItem!, language: ProjectSetup.Languages[language.SelectedIndex])); }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException) { error.Text = e.Message; }
        };
        var form = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = L.T("The project starts untitled. Give it a name and a place with File › Save (or Save As…)."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
                Label(L.T("Template")), template,
                Label(L.T("Language")), language,
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { create, cancel } },
            },
        };
        return new DockPanel { Children = { Header(L.T("New Project")), new ScrollViewer { Content = form } } };
    }

    /// <summary>Picks a folder; one without faro.json is initialized after confirmation (existing files are never changed).</summary>
    public static async Task OpenFolder(Window owner, Action<string> open)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new() { Title = L.T("Open Folder") });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir) return;
        if (!ProjectSetup.IsFaroProject(dir))
        {
            if (!await Dialogs.Confirm(owner, L.T("Open Folder"), L.F("{0} isn't a Faro project yet. Initialize it? Missing folders and files are added; existing files aren't changed.", dir), L.T("Initialize")))
                return;
            try { ProjectSetup.Initialize(dir, language: File.Exists(Path.Combine(dir, "pom.xml")) ? JavaProject.Language : "CSharp"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                await Dialogs.Info(owner, L.T("Open Folder"), new TextBlock { Text = e.Message, TextWrapping = TextWrapping.Wrap });
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
