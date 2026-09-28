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
        if (FaroSettings.Current.SetupDone) Start(desktop, null);
        else
        {
            // First run: the setup wizard (language, theme, plugins), then the usual start.
            var setup = new SetupWindow();
            setup.Done += () => Start(desktop, setup);
            desktop.MainWindow = setup;
            setup.Show();
        }
    }

    static void Start(IClassicDesktopStyleApplicationLifetime desktop, Window? previous)
    {
        if (root is not null) { OpenProject(desktop, root, previous); return; }
        var welcome = new WelcomeWindow();
        welcome.ProjectChosen += dir => OpenProject(desktop, dir, welcome);
        desktop.MainWindow = welcome;
        welcome.Show();
        previous?.Close();
        Updates.Offer(welcome, manual: false);
        Log.OfferCrashReport(welcome);
    }

    /// <summary>Splash with loading progress, then the editor on the project (spec §4 folder).</summary>
    static async void OpenProject(IClassicDesktopStyleApplicationLifetime desktop, string dir, Window? previous)
    {
        ProjectSetup.Remember(dir);
        var splash = new SplashWindow();
        desktop.MainWindow = splash;
        splash.Show();
        previous?.Close(); // after the splash is up, so closing the last window doesn't end the app
        // Workspace trust, before anything runs the project's code (restore, language server, Script previews).
        if (!ProjectSetup.IsTrusted(dir) && await Dialogs.Choose(splash, L.T("Trust this project?"),
                $"{dir}\n\n" + L.T("Faro restores, builds and runs a project's code (language server, Script previews, Run). Trust it only if you know where it comes from. In restricted mode you can still view and edit it."),
                [L.T("Trust"), L.T("Restricted Mode")], cancel: false) == 0)
            ProjectSetup.Trust(dir);
        await Task.Run(() => Workspace.Open(dir, splash.Report));
        var updated = false;
        try
        {
            if (ProjectSetup.RuntimeUpdateAvailable(dir) && await Dialogs.Confirm(splash, L.T("Faro.Runtime update"),
                L.F("This project uses Faro.Runtime {0}. This Faro ships {1}. Update the project to it?", ProjectSetup.ProjectRuntime(dir), ProjectSetup.BundledRuntime().Version), L.T("Update")))
            {
                ProjectSetup.UpdateRuntime(dir);
                updated = true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { } // keep the project's runtime
        // A new, copied or just-updated project needs a restore: the language server resolves Faro.Runtime from it.
        if (Workspace.Trusted && (updated || !File.Exists(Path.Combine(dir, "obj", "project.assets.json"))) && Directory.EnumerateFiles(dir, "*.csproj").Any())
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
        // ponytail: C# only; Java files get highlighting (a Java server, jdtls, when completion is needed there)
        if (!Workspace.IsJava) splash.Report("Starting C# language server…", 80);
        try { if (Workspace.Trusted && !Workspace.IsJava) await CodeView.Lsp(); }
        catch (Exception) { } // the code pane shows why; the editor still opens
        splash.Report("Ready", 100);

        desktop.MainWindow = new MainWindow { Title = $"Faro — {Path.GetFileName(dir)}{(ProjectSetup.IsUntitled(dir) ? L.T(" (not saved)") : "")}{(Workspace.Trusted ? "" : L.T(" (Restricted Mode)"))}" };
        desktop.MainWindow.Show();
        splash.Close();
        Updates.Offer(desktop.MainWindow, manual: false);
        Log.OfferCrashReport(desktop.MainWindow);
    }

    [STAThread]
    public static void Main(string[] args)
    {
        Log.Start();
        root = args.FirstOrDefault() is { } path ? Path.GetFullPath(path) : null; // no folder given: welcome screen
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
