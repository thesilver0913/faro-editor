using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>
/// Crash reporting from a second process, like Adobe's crash reporter: Faro starts itself again as `--watch &lt;pid&gt;`, which only
/// waits (no UI loaded, a few MB) until the editor ends. A clean exit deletes the editor's "running" marker; if it is still there,
/// the editor crashed — also where .NET's handler never ran (native crash, out of memory, killed) — and the watcher shows the report at once.
/// </summary>
public static class CrashWatcher
{
    static string Marker(string folder, int pid) => Path.Combine(folder, $"running-{pid}");
    static PosixSignalRegistration[] signals = [];

    /// <summary>In the editor, at start: the marker (deleted on a clean exit) and the watcher process.</summary>
    public static void Spawn()
    {
        var pid = Environment.ProcessId;
        try
        {
            Directory.CreateDirectory(Log.Folder);
            File.WriteAllText(Marker(Log.Folder, pid), "");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(Marker(Log.Folder, pid)); // not raised for a crash
            Process.Start(Self(["--watch", pid.ToString()]))?.Dispose();
            // Asked to quit (logout, shutdown, Ctrl+C): not a crash either.
            signals = [.. new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP }
                .Select(signal => PosixSignalRegistration.Create(signal, _ => File.Delete(Marker(Log.Folder, pid))))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { Log.Error("Crash watcher", e); }
    }

    /// <summary>Faro's own command line with other arguments (`dotnet Faro.Editor.dll …` when run through the dotnet host).</summary>
    public static ProcessStartInfo Self(string[] args)
    {
        var host = Environment.ProcessPath!;
        return new(host, Path.GetFileNameWithoutExtension(host) == "dotnet" ? [typeof(App).Assembly.Location, .. args] : args) { UseShellExecute = false };
    }

    /// <summary>In the watcher: waits for the editor; the crash report to show, or null after a clean exit.</summary>
    public static string? Watch(int pid)
    {
        try { using var editor = Process.GetProcessById(pid); editor.WaitForExit(); }
        catch (ArgumentException) { } // already gone
        return Outcome(Log.Folder, pid);
    }

    /// <summary>
    /// After the editor ended: null if its marker is gone (clean exit). Otherwise the crash report it wrote since it started,
    /// or a new one saying no .NET exception was recorded; the marker is removed.
    /// </summary>
    public static string? Outcome(string folder, int pid)
    {
        var marker = Marker(folder, pid);
        if (!File.Exists(marker)) return null;
        var started = File.GetLastWriteTimeUtc(marker);
        File.Delete(marker);
        var report = Directory.GetFiles(folder, "crash-*.txt").Where(f => File.GetLastWriteTimeUtc(f) >= started).MaxBy(File.GetLastWriteTimeUtc);
        return report ?? Log.Crash(new Exception("Faro ended without a .NET exception (a native crash, out of memory, or the process was killed)."), folder);
    }

    /// <summary>The report with the end of the log before the crash.</summary>
    public static string Details(string report)
    {
        var log = Directory.GetFiles(Path.GetDirectoryName(report)!, "faro-*.log").MaxBy(File.GetLastWriteTimeUtc);
        return File.ReadAllText(report).TrimEnd() + (log is null ? "" : $"\n\n--- {Path.GetFileName(log)} (last 40 lines) ---\n" + string.Join('\n', File.ReadLines(log).TakeLast(40)));
    }

    /// <summary>"Faro quit unexpectedly": what the user was doing, the details, Report Issue / Copy / Restart / Close.</summary>
    public static Window Window(string report)
    {
        var details = Details(report);
        var doing = new TextBox { PlaceholderText = L.T("What were you doing when Faro quit? (optional)"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72 };
        var window = new Window { Title = L.T("Faro quit unexpectedly"), Width = 640, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        Button Action(string text, Action act, bool accent = false)
        {
            var button = new Button { Content = L.T(text) };
            if (accent) button.Classes.Add("accent");
            button.Click += (_, _) => act();
            return button;
        }
        async Task Copy() { if (window.Clipboard is { } clipboard) await clipboard.SetTextAsync(details); }
        window.Content = new StackPanel
        {
            Spacing = 12, Margin = new(20),
            Children =
            {
                new TextBlock { Text = L.T("Faro quit unexpectedly"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = L.T("A crash report was saved. Sending it as an issue helps fix the problem. Nothing is sent without you."), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                doing,
                new Expander
                {
                    Header = L.T("Details"), HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, Height = 240, FontFamily = FontFamily.Parse(App.CodeFont), FontSize = 12 },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children =
                    {
                        Action("Report Issue", async () => { await Copy(); Updates.Open(IssueUrl(doing.Text, details, report)); }, accent: true),
                        Action("Copy Details", async () => await Copy()),
                        Action("Restart Faro", () => { Process.Start(Self([]))?.Dispose(); window.Close(); }),
                        Action("Close", window.Close),
                    },
                },
            },
        };
        return window;
    }

    /// <summary>A new GitHub issue with the description and the details (cut to keep the URL short; the full text is on the clipboard).</summary>
    public static string IssueUrl(string? doing, string details, string report)
    {
        const int limit = 4000;
        var body = $"{(string.IsNullOrWhiteSpace(doing) ? L.T("(Describe what you were doing.)") : doing.Trim())}\n\n```\n{(details.Length > limit ? details[..limit] + "\n… " + L.T("(cut: paste the full details from the clipboard)") : details)}\n```";
        return $"https://github.com/{Updates.Repository}/issues/new?title={Uri.EscapeDataString("Crash: " + Path.GetFileName(report))}&body={Uri.EscapeDataString(body)}";
    }
}
