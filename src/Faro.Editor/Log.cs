using System.Runtime.InteropServices;

namespace Faro.Editor;

/// <summary>
/// Daily log files and crash reports in the settings folder's "logs" (AppData/Faro/logs), kept 14 days.
/// Local only: nothing is sent anywhere; the user attaches a report to an issue if they want.
/// </summary>
public static class Log
{
    public static string Folder => Path.Combine(Path.GetDirectoryName(FaroSettings.FilePath)!, "logs");
    static readonly Lock gate = new();

    public static void Info(string message) => Write("INFO ", message);
    public static void Error(string what, Exception e) => Write("ERROR", $"{what}: {e}");

    static void Write(string level, string message)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"faro-{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // logging never takes Faro down
    }

    /// <summary>Starts the session log, drops old files and saves a crash report for any unhandled exception.</summary>
    public static void Start()
    {
        try
        {
            foreach (var old in Directory.Exists(Folder) ? Directory.GetFiles(Folder) : [])
                if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-14)) File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Info($"Faro {App.Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}), .NET {Environment.Version}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { Error("Unobserved task exception", e.Exception); e.SetObserved(); };
    }

    public static string? Crash(Exception e)
    {
        Error("Crash", e);
        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, $"""
                Faro {App.Version}
                OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})
                .NET: {Environment.Version}
                Time: {DateTimeOffset.Now:O}

                {e}
                """);
            return file;
        }
        catch (Exception io) when (io is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The newest crash report from an earlier run the user hasn't been shown yet.</summary>
    public static string? UnseenCrash()
    {
        if (!Directory.Exists(Folder)) return null;
        var newest = Directory.GetFiles(Folder, "crash-*.txt").MaxBy(File.GetLastWriteTimeUtc);
        return newest is not null && File.GetLastWriteTimeUtc(newest) > FaroSettings.Current.CrashesSeen ? newest : null;
    }

    /// <summary>At startup: "Faro quit unexpectedly" with the report and a prefilled GitHub issue.</summary>
    public static async void OfferCrashReport(Avalonia.Controls.Window owner)
    {
        if (UnseenCrash() is not { } report) return;
        FaroSettings.Current.CrashesSeen = DateTime.UtcNow;
        FaroSettings.Current.Save();
        switch (await Dialogs.Choose(owner, L.T("Faro quit unexpectedly"), L.T("A crash report was saved. Attaching it to an issue helps fix the problem.") + "\n\n" + report,
                    [L.T("Open Report"), L.T("Report Issue")]))
        {
            case 0: Updates.Open(report); break;
            case 1: Updates.Open($"https://github.com/{Updates.Repository}/issues/new?title={Uri.EscapeDataString("Crash: " + Path.GetFileName(report))}&body={Uri.EscapeDataString(L.T("Please attach the crash report and describe what you were doing.") + $"\n\nFaro {App.Version}")}"); break;
        }
    }
}
