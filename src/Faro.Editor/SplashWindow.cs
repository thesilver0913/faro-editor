using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Faro.Editor;

/// <summary>Borderless startup splash (GIMP/Adobe style) with the current loading step and a progress bar over the artwork.</summary>
public sealed class SplashWindow : Window
{
    readonly TextBlock step = new() { Text = "Starting…", Foreground = Brushes.White, FontSize = 13, Opacity = 0.9 };
    readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 3, MinHeight = 3, Foreground = new SolidColorBrush(Color.Parse("#1473E6")), Background = new SolidColorBrush(Colors.White, 0.25) };

    public SplashWindow()
    {
        Width = 640;
        Height = 360;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Faro";
        Content = new Grid
        {
            Children =
            {
                new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Faro.Editor/Assets/faro-splash.png"))), Stretch = Stretch.UniformToFill },
                // The version under the "Faro" wordmark of the artwork, in Inter (OFL, bundled).
                new TextBlock
                {
                    Text = App.Version,
                    FontFamily = new FontFamily("avares://Faro.Editor/Assets/Fonts#Inter"),
                    FontSize = 22,
                    Foreground = new SolidColorBrush(Color.Parse("#7A7C80")),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new(59, 182, 0, 0),
                },
                new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 8, Margin = new(24, 0, 24, 20), Children = { step, bar } },
            },
        };
    }

    /// <summary>Thread-safe: startup steps report from background threads.</summary>
    public void Report(string text, double percent) => Dispatcher.UIThread.Post(() =>
    {
        step.Text = text;
        bar.Value = percent;
    });
}
