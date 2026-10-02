using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Faro.Editor;

public partial class App : Application
{
    /// <summary>"0.1.8" or "0.1.8-dev1" (without the commit hash the SDK appends).</summary>
    public static string Version => Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    static string? Meta(string key) => Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    /// <summary>
    /// Faro Canary: a separate app built from every merge into canary (-p:FaroEdition=Canary -p:FaroBuild=N): its own
    /// name, icon, settings folder and updates (the newest canary build). Its Faro.Runtime keeps the normal version.
    /// </summary>
    public static bool IsCanary => Meta("FaroEdition") == "Canary";
    public static int Build => int.TryParse(Meta("FaroBuild"), out var build) ? build : 0;
    public static string Name => IsCanary ? "Faro Canary" : "Faro";
    public static string DisplayVersion => IsCanary ? $"{Version} (build {Build})" : Version;

    static string? root, crashReport;

    /// <summary>The bundled UI font, the same on every OS.</summary>
    public const string UiFont = "avares://Faro.Editor/Assets/Fonts#Noto Sans JP";

    /// <summary>Code and other monospaced text: the first installed of the usual programming fonts.</summary>
    public const string CodeFont = "Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,Liberation Mono,Noto Sans Mono,monospace";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        FaroSettings.Current.ApplyTheme();
        if (crashReport is not null) // the crash watcher: only the report
        {
            desktop.MainWindow = CrashWatcher.Window(crashReport);
            desktop.MainWindow.Show();
            return;
        }
        // A file Faro can't read or write (locked by another program, no permission) is reported, not a crash.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (e.Exception is not (IOException or UnauthorizedAccessException)) return;
            e.Handled = true;
            Log.Error("File access", e.Exception);
            if (desktop.MainWindow is { } owner)
                _ = Dialogs.Info(owner, L.T("Couldn't access a file"), new TextBlock { Text = e.Exception.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        };
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
                using var restore = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", ["restore"]) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
                await Task.WhenAll(restore.StandardOutput.ReadToEndAsync(), restore.StandardError.ReadToEndAsync(), restore.WaitForExitAsync()); // both pipes drained: a full one would hang the splash
            }
            catch (System.ComponentModel.Win32Exception) { } // no dotnet on PATH: the code pane will show unresolved references
        }
        // ponytail: C# only; Java files get highlighting (a Java server, jdtls, when completion is needed there)
        if (!Workspace.IsJava) splash.Report("Starting C# language server…", 80);
        try { if (Workspace.Trusted && !Workspace.IsJava) await CodeView.Lsp(); }
        catch (Exception) { } // the code pane shows why; the editor still opens
        splash.Report("Ready", 100);

        desktop.MainWindow = new MainWindow { Title = $"{Name} — {Path.GetFileName(dir)}{(ProjectSetup.IsUntitled(dir) ? L.T(" (not saved)") : "")}{(Workspace.Trusted ? "" : L.T(" (Restricted Mode)"))}" };
        desktop.MainWindow.Show();
        splash.Close();
        Updates.Offer(desktop.MainWindow, manual: false);
        Log.OfferCrashReport(desktop.MainWindow);
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--version"]) // the installers' smoke test: the launcher found .NET and Faro starts
        {
            Console.WriteLine($"{Name} {DisplayVersion}");
            return;
        }
        if (args is ["--watch", var pid] && int.TryParse(pid, out var editor)) // the crash watcher waits, then shows the report if the editor crashed
        {
            if ((crashReport = CrashWatcher.Watch(editor)) is null) return;
            Log.MarkCrashesSeen();
            args = [];
        }
        else Log.Start();
        if (!OperatingSystem.IsWindows()) ImportShellPath();
        // The .NET SDK the installer put next to Faro also builds and runs the projects (dotnet restore / watch run).
        var bundled = Path.Combine(AppContext.BaseDirectory, "dotnet");
        if (File.Exists(Path.Combine(bundled, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")))
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", bundled);
            Environment.SetEnvironmentVariable("PATH", bundled + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        }
        Components.Activate(); // JDK, Maven, GraalVM installed from Preferences › Components
        if (OperatingSystem.IsMacOS()) // Finder's PATH is only /usr/bin:/bin:…: also where dotnet, Maven and Homebrew install, in case the shell didn't say
            Environment.SetEnvironmentVariable("PATH", string.Join(':', Environment.GetEnvironmentVariable("PATH"), "/usr/local/share/dotnet", "/usr/local/bin", "/opt/homebrew/bin",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet")));
        if (args is ["--build-apk", var folder]) // headless, e.g. CI: `Faro.Editor --build-apk <project folder>`
        {
            var apk = AndroidApk.BuildAsync(Path.GetFullPath(folder), Console.WriteLine).GetAwaiter().GetResult();
            Console.WriteLine(apk is null ? "APK build failed." : $"APK: {apk}");
            Environment.Exit(apk is null ? 1 : 0);
        }
        if (crashReport is null) CrashWatcher.Spawn();
        root = args.FirstOrDefault() is { } path ? Path.GetFullPath(path) : null; // no folder given: welcome screen
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = UiFont })
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Linux / macOS: an app started from the menu, dock or Finder doesn't get the PATH set in shell profiles (SDKMAN's Maven
    /// and JDK, ~/.dotnet, Homebrew…), so `mvn` or `dotnet` "can't be started". Asks the login shell for its PATH, like VS Code.
    /// </summary>
    static void ImportShellPath()
    {
        const string marker = "__FARO_PATH__";
        try
        {
            var path = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
            var print = path.EndsWith("fish") ? $"printf '{marker}%s' (string join : $PATH)" : $"printf '{marker}%s' \"$PATH\""; // fish's PATH is a list
            using var shell = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, ["-ilc", print])
                { RedirectStandardOutput = true, RedirectStandardInput = true, CreateNoWindow = true })!;
            var output = shell.StandardOutput.ReadToEndAsync();
            if (!output.Wait(TimeSpan.FromSeconds(3))) { shell.Kill(entireProcessTree: true); return; } // a slow or waiting profile: keep ours
            var at = output.Result.LastIndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return;
            var ours = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
            var theirs = output.Result[(at + marker.Length)..].Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
            Environment.SetEnvironmentVariable("PATH", string.Join(':', theirs.Concat(ours).Distinct()));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Error("Shell PATH", e); }
    }
}
