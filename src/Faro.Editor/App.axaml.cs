using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Faro.Editor;

public partial class App : Application
{
    static string root = "";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var splash = new SplashWindow();
        desktop.MainWindow = splash;
        splash.Show();
        await Task.Run(() => Workspace.Open(root, splash.Report));
        splash.Report("Starting C# language server…", 80);
        try { await CodeView.Lsp(); }
        catch (Exception) { } // the code pane shows why; the editor still opens
        splash.Report("Ready", 100);

        desktop.MainWindow = new MainWindow();
        desktop.MainWindow.Show();
        splash.Close();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
