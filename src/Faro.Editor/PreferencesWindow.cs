using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TextMateSharp.Grammars;

namespace Faro.Editor;

/// <summary>Edit ▸ Preferences: environment variables (read-only status), themes, plugins (placeholder).</summary>
public sealed class PreferencesWindow : Window
{
    static readonly string[] EnvironmentVariables = ["ANTHROPIC_API_KEY", "OPENAI_API_KEY"];

    readonly FaroSettings settings = FaroSettings.Current;
    readonly TextBlock themeError = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };

    public PreferencesWindow()
    {
        Title = "Preferences";
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TabControl
        {
            Margin = new(12),
            TabStripPlacement = Avalonia.Controls.Dock.Left,
            Items =
            {
                new TabItem { Header = "Environment", Content = EnvironmentPage() },
                new TabItem { Header = "Theme", Content = ThemePage() },
                new TabItem { Header = "Plugins", Content = Page(new TextBlock { Text = "Plugins are planned for a later version (spec §12: outside the prototype scope).", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }) },
            },
        };
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
            var name = new SelectableTextBlock { Text = EnvironmentVariables[i], FontFamily = FontFamily.Parse("Cascadia Code,Consolas,Menlo,monospace") };
            var state = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? "Not set" : $"Set (…{value[^Math.Min(4, value.Length)..]})",
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
            new TextBlock { Text = "API keys are read from environment variables only. Faro never writes them to disk.", TextWrapping = TextWrapping.Wrap },
            grid,
            new TextBlock { Text = "To set one:", Opacity = 0.7 },
            new SelectableTextBlock { Text = howTo, FontFamily = FontFamily.Parse("Cascadia Code,Consolas,Menlo,monospace"), TextWrapping = TextWrapping.Wrap });
    }

    Control ThemePage()
    {
        var appTheme = new ComboBox { ItemsSource = new[] { "System", "Dark", "Light" }, SelectedItem = settings.AppTheme, MinWidth = 160 };
        appTheme.SelectionChanged += (_, _) => { settings.AppTheme = (string)appTheme.SelectedItem!; Apply(); };

        var editorThemes = new List<string> { FaroSettings.BuiltInEditorTheme };
        editorThemes.AddRange(Enum.GetNames<ThemeName>());
        editorThemes.Add(FaroSettings.CustomEditorTheme);
        var editorTheme = new ComboBox { ItemsSource = editorThemes, SelectedItem = settings.EditorTheme, MinWidth = 220 };
        var editorFile = FileRow(settings.EditorThemeFile, "Code editor theme", ["*.tmTheme", "*.json"], path =>
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
            new TextBlock { Text = "App theme", FontWeight = FontWeight.SemiBold },
            appTheme,
            new TextBlock { Text = "Custom theme file (Avalonia ResourceDictionary .axaml, overrides the colors above)", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
            FileRow(settings.AppThemeFile, "App theme", ["*.axaml", "*.xaml"], path => { settings.AppThemeFile = path; Apply(); }),
            new TextBlock { Text = "Code editor theme", FontWeight = FontWeight.SemiBold, Margin = new(0, 12, 0, 0) },
            editorTheme,
            editorFile,
            themeError);
    }

    /// <summary>Path box with Browse / Clear; calls <paramref name="changed"/> with the new path ("" when cleared).</summary>
    Control FileRow(string path, string title, string[] patterns, Action<string> changed)
    {
        var box = new TextBox { Text = path, IsReadOnly = true, PlaceholderText = "(none)" };
        var browse = new Button { Content = "Browse…" };
        var clear = new Button { Content = "Clear" };
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
