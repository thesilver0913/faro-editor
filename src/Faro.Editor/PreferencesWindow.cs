using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TextMateSharp.Grammars;

namespace Faro.Editor;

/// <summary>Edit ▸ Preferences: environment variables (read-only status), themes, plugins (coming later).</summary>
public sealed class PreferencesWindow : Window
{
    static readonly string[] EnvironmentVariables = ["ANTHROPIC_API_KEY", "OPENAI_API_KEY"];

    readonly FaroSettings settings = FaroSettings.Current;
    readonly TextBlock themeError = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };

    public PreferencesWindow()
    {
        Title = L.T("Preferences");
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TabControl
        {
            Margin = new(12),
            TabStripPlacement = Avalonia.Controls.Dock.Left,
            Items =
            {
                new TabItem { Header = L.T("Language"), Content = Page(LanguageChoice(null), new TextBlock { Text = L.T("Language changes apply to windows opened from now on; restart Faro to update the menus."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }) },
                new TabItem { Header = L.T("Environment"), Content = EnvironmentPage() },
                new TabItem { Header = L.T("Tools"), Content = ComponentsPage() },
                new TabItem { Header = L.T("Updates"), Content = UpdatesPage() },
                new TabItem { Header = L.T("Theme"), Content = ThemePage() },
                new TabItem { Header = L.T("Plugins"), Content = Page(new TextBlock { Text = L.T("Plugins are coming in a later version."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }) },
            },
        };
    }

    /// <summary>English / 日本語 (also in the first-run wizard, which redraws itself through <paramref name="changed"/>).</summary>
    public static ComboBox LanguageChoice(Action? changed)
    {
        var box = new ComboBox { ItemsSource = L.Languages, SelectedIndex = FaroSettings.Current.Language == "ja" ? 1 : 0, MinWidth = 160 };
        box.SelectionChanged += (_, _) =>
        {
            FaroSettings.Current.Language = box.SelectedIndex == 1 ? "ja" : "en";
            FaroSettings.Current.Save();
            changed?.Invoke();
        };
        return box;
    }

    /// <summary>Update channel (Stable = main, Beta; Faro Canary follows the canary builds), the startup check, and a manual check.</summary>
    Control UpdatesPage()
    {
        var channel = new ComboBox { ItemsSource = Updates.Channels.Select(L.T).ToArray(), SelectedIndex = Array.IndexOf(Updates.Channels, Updates.Channel), MinWidth = 160, IsVisible = !App.IsCanary };
        channel.SelectionChanged += (_, _) => { settings.UpdateChannel = Updates.Channels[channel.SelectedIndex]; settings.Save(); };
        var onStart = new CheckBox { Content = L.T("Check for updates at startup (daily)"), IsChecked = settings.CheckUpdatesOnStart };
        onStart.IsCheckedChanged += (_, _) => { settings.CheckUpdatesOnStart = onStart.IsChecked == true; settings.Save(); };
        var now = new Button { Content = L.T("Check Now") };
        now.Click += (_, _) => Updates.Offer(this, manual: true);
        return Page(
            new TextBlock { Text = L.T("Update channel"), FontWeight = FontWeight.SemiBold }, channel,
            new TextBlock { Text = App.IsCanary ? L.T("Faro Canary updates to the newest development build (one is made from every change merged into canary). It keeps its own settings, next to Faro.")
                : L.T("Stable: releases. Beta: previews of the next release. For the latest development builds, install Faro Canary (a separate app) from the releases page."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
            onStart, now,
            new TextBlock { Text = L.F("Current version: {0}. Releases come from github.com/{1}; the check only reads public release data.", App.DisplayVersion, Updates.Repository), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
    }

    /// <summary>On-demand tools (JDK, Maven, GraalVM, Android workload): status and an Install button each; the last output line below.</summary>
    static Control ComponentsPage()
    {
        var log = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse(App.CodeFont), FontSize = 12, Opacity = 0.8 };
        var grid = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 12, RowSpacing = 10 };
        foreach (var component in Components.All.Where(c => c.Available))
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new(GridLength.Auto));
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var install = new Button { Content = L.T("Install") };
            void Show()
            {
                var installed = component.Installed();
                status.Text = L.T(installed ? "Installed" : "Not installed");
                install.IsEnabled = !installed;
            }
            Show();
            install.Click += async (_, _) =>
            {
                install.IsEnabled = false;
                if (await component.Install(line => Avalonia.Threading.Dispatcher.UIThread.Post(() => log.Text = line), CancellationToken.None)) Components.Activate();
                Show();
            };
            var name = new StackPanel { Children = { new TextBlock { Text = component.Name, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = L.T(component.Purpose), Opacity = 0.7, TextWrapping = TextWrapping.Wrap } } };
            Grid.SetRow(name, row);
            Grid.SetRow(status, row);
            Grid.SetColumn(status, 1);
            Grid.SetRow(install, row);
            Grid.SetColumn(install, 2);
            grid.Children.AddRange([name, status, install]);
        }
        return Page(new TextBlock { Text = L.T("Tools Faro downloads when you need them. They go into Faro's own folder and are used only by Faro."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }, grid, log);
    }

    static Control Page(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new(16, 4) };
        panel.Children.AddRange(children);
        return new ScrollViewer { Content = panel };
    }

    /// <summary>Shows whether each variable is set, masked. Faro never stores keys, so this page cannot edit them.</summary>
    static Control EnvironmentPage()
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 16, RowSpacing = 8 };
        for (var i = 0; i < EnvironmentVariables.Length; i++)
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariables[i]);
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new SelectableTextBlock { Text = EnvironmentVariables[i], FontFamily = FontFamily.Parse(App.CodeFont) };
            var state = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? L.T("Not set") : L.F("Set (…{0})", value[^Math.Min(4, value.Length)..]),
                Foreground = string.IsNullOrEmpty(value) ? Brushes.OrangeRed : Brushes.LightGreen,
            };
            Grid.SetRow(name, i);
            Grid.SetRow(state, i);
            Grid.SetColumn(state, 1);
            grid.Children.Add(name);
            grid.Children.Add(state);
        }
        var howTo = OperatingSystem.IsWindows()
            ? "setx ANTHROPIC_API_KEY \"your-key\"   (Command Prompt; then restart Faro)"
            : "export ANTHROPIC_API_KEY=\"your-key\"   (add to ~/.zshrc or ~/.bashrc; then restart Faro)";
        return Page(
            new TextBlock { Text = L.T("API keys are read from environment variables only. Faro never writes them to disk."), TextWrapping = TextWrapping.Wrap },
            grid,
            new TextBlock { Text = L.T("To set one:"), Opacity = 0.7 },
            new SelectableTextBlock { Text = howTo, FontFamily = FontFamily.Parse(App.CodeFont), TextWrapping = TextWrapping.Wrap });
    }

    Control ThemePage()
    {
        var appTheme = new ComboBox { ItemsSource = new[] { "System", "Dark", "Light" }, SelectedItem = settings.AppTheme, MinWidth = 160 };
        appTheme.SelectionChanged += (_, _) => { settings.AppTheme = (string)appTheme.SelectedItem!; Apply(); };

        var editorThemes = new List<string> { FaroSettings.BuiltInEditorTheme };
        editorThemes.AddRange(Enum.GetNames<ThemeName>());
        editorThemes.Add(FaroSettings.CustomEditorTheme);
        var editorTheme = new ComboBox { ItemsSource = editorThemes, SelectedItem = settings.EditorTheme, MinWidth = 220 };
        var editorFile = FileRow(settings.EditorThemeFile, L.T("Code editor theme"), ["*.tmTheme", "*.json"], path =>
        {
            settings.EditorThemeFile = path;
            if (path.Length > 0) editorTheme.SelectedItem = FaroSettings.CustomEditorTheme;
            Apply();
        });
        editorFile.IsVisible = settings.EditorTheme == FaroSettings.CustomEditorTheme;
        editorTheme.SelectionChanged += (_, _) =>
        {
            settings.EditorTheme = (string)editorTheme.SelectedItem!;
            editorFile.IsVisible = settings.EditorTheme == FaroSettings.CustomEditorTheme;
            Apply();
        };

        return Page(
            new TextBlock { Text = L.T("App theme"), FontWeight = FontWeight.SemiBold },
            appTheme,
            new TextBlock { Text = L.T("Custom theme file (Avalonia ResourceDictionary .axaml, overrides the colors above)"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
            FileRow(settings.AppThemeFile, L.T("App theme"), ["*.axaml", "*.xaml"], path => { settings.AppThemeFile = path; Apply(); }),
            new TextBlock { Text = L.T("Code editor theme"), FontWeight = FontWeight.SemiBold, Margin = new(0, 12, 0, 0) },
            editorTheme,
            editorFile,
            themeError);
    }

    /// <summary>Path box with Browse / Clear; calls <paramref name="changed"/> with the new path ("" when cleared).</summary>
    Control FileRow(string path, string title, string[] patterns, Action<string> changed)
    {
        var box = new TextBox { Text = path, IsReadOnly = true, PlaceholderText = L.T("(none)") };
        var browse = new Button { Content = L.T("Browse…") };
        var clear = new Button { Content = L.T("Clear") };
        browse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = title, FileTypeFilter = [new(title) { Patterns = patterns }] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } picked) { box.Text = picked; changed(picked); }
        };
        clear.Click += (_, _) => { box.Text = ""; changed(""); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(8, 0, 0, 0), Children = { browse, clear } };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Right);
        return new DockPanel { Children = { buttons, box } };
    }

    void Apply()
    {
        settings.Save();
        themeError.Text = settings.ApplyTheme() ?? "";
    }
}
