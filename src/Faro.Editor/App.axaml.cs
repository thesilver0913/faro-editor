using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Faro.Editor;

public partial class App : Application
{
    static string? root;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        FaroSettings.Current.ApplyTheme();
        if (root is not null) { OpenProject(desktop, root, null); return; }
        var welcome = new WelcomeWindow();
        welcome.ProjectChosen += dir => OpenProject(desktop, dir, welcome);
        desktop.MainWindow = welcome;
        welcome.Show();
    }

    /// <summary>Splash with loading progress, then the editor on the project (spec §4 folder).</summary>
    static async void OpenProject(IClassicDesktopStyleApplicationLifetime desktop, string dir, Window? previous)
    {
        ProjectSetup.Remember(dir);
        var splash = new SplashWindow();
        desktop.MainWindow = splash;
        splash.Show();
        previous?.Close(); // after the splash is up, so closing the last window doesn't end the app
        await Task.Run(() => Workspace.Open(dir, splash.Report));
        // A new or copied project has no restored packages yet: the language server needs them to resolve Faro.Runtime.
        if (!File.Exists(Path.Combine(dir, "obj", "project.assets.json")) && Directory.EnumerateFiles(dir, "*.csproj").Any())
        {
            splash.Report("Restoring packages…", 70);
            try
            {
                using var restore = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", ["restore"]) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true })!;
                await restore.StandardOutput.ReadToEndAsync();
                await restore.WaitForExitAsync();
            }
            catch (System.ComponentModel.Win32Exception) { } // no dotnet on PATH: the code pane will show unresolved references
        }
        splash.Report("Starting C# language server…", 80);
        try { await CodeView.Lsp(); }
        catch (Exception) { } // the code pane shows why; the editor still opens
        splash.Report("Ready", 100);

        desktop.MainWindow = new MainWindow { Title = $"Faro — {Path.GetFileName(dir)}" };
        desktop.MainWindow.Show();
        splash.Close();
    }

    [STAThread]
    public static void Main(string[] args)
    {
        root = args.FirstOrDefault() is { } path ? Path.GetFullPath(path) : null; // no folder given: welcome screen
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
