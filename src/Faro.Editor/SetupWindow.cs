using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Faro.Editor;

/// <summary>First-run wizard: language (applied at once), app theme, and the plugins note. All of it stays editable in Preferences.</summary>
public sealed class SetupWindow : Window
{
    public event Action? Done;

    public SetupWindow()
    {
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png")));
        Width = 560;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Build();
    }

    /// <summary>Rebuilt when the language changes, so the wizard itself switches language.</summary>
    void Build()
    {
        var settings = FaroSettings.Current;
        Title = L.T("Set up Faro");
        string[] themes = ["Dark", "Light", "System"];
        var theme = new ComboBox { ItemsSource = themes.Select(L.T).ToArray(), SelectedIndex = Math.Max(Array.IndexOf(themes, settings.AppTheme), 0), MinWidth = 160 };
        theme.SelectionChanged += (_, _) =>
        {
            settings.AppTheme = themes[theme.SelectedIndex];
            settings.ApplyTheme();
        };
        var start = new Button { Content = L.T("Start"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "accent" } };
        start.Click += (_, _) =>
        {
            settings.SetupDone = true;
            settings.Save();
            Done?.Invoke();
        };
        TextBlock Label(string text) => new() { Text = L.T(text), FontWeight = FontWeight.SemiBold };
        Content = new StackPanel
        {
            Spacing = 10,
            Margin = new(28),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 12,
                    Children =
                    {
                        new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-icon.png"))), Width = 56, Height = 56 },
                        new TextBlock { Text = L.T("Set up Faro"), FontSize = 24, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                new TextBlock { Text = L.T("Choose how Faro looks. You can change all of this later in Preferences."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                Label("Language"), PreferencesWindow.LanguageChoice(Build),
                Label("Theme"), theme,
                Label("Plugins"), new TextBlock { Text = L.T("Plugins are planned for a later version (spec §12: outside the prototype scope)."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                start,
            },
        };
    }
}
