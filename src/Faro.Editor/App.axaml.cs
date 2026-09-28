using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Faro.Editor;

public partial class App : Application
{
    /// <summary>"0.1.8" or "0.1.8-dev1" (without the commit hash the SDK appends).</summary>
    public static string Version => Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    static string? root;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        FaroSettings.Current.ApplyTheme();
        // Discarded untitled projects go once no earlier Faro process holds their files.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => ProjectSetup.DeletePending(Workspace.Root.Length > 0 ? Workspace.Root : null)));
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
        var updated = false;
        try
        {
            if (ProjectSetup.RuntimeUpdateAvailable(dir) && await Dialogs.Confirm(splash, "Faro.Runtime update",
                $"This project uses Faro.Runtime {ProjectSetup.ProjectRuntime(dir)}. This Faro ships {ProjectSetup.BundledRuntime().Version}. Update the project to it?", "Update"))
            {
                ProjectSetup.UpdateRuntime(dir);
                updated = true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { } // keep the project's runtime
        // A new, copied or just-updated project needs a restore: the language server resolves Faro.Runtime from it.
        if ((updated || !File.Exists(Path.Combine(dir, "obj", "project.assets.json"))) && Directory.EnumerateFiles(dir, "*.csproj").Any())
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

        desktop.MainWindow = new MainWindow { Title = $"Faro — {Path.GetFileName(dir)}{(ProjectSetup.IsUntitled(dir) ? " (not saved)" : "")}" };
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
