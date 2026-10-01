using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Faro.Editor;

/// <summary>
/// Update check against GitHub Releases. Channels (Preferences): Stable = releases (main), Beta = + "-beta.N" pre-releases.
/// Faro Canary (a separate app) follows the "canary-N" releases the workflow makes from every merge into canary. Windows installs by running the new Setup (Inno Setup upgrades in place and
/// restarts Faro); on Linux and macOS the release page opens for the .deb / archive / disk image. Only reads public release data; sends nothing.
/// </summary>
public static class Updates
{
    public const string Repository = "thesilver0913/faro-editor";
    public static readonly string[] Channels = ["Stable", "Beta"];

    /// <summary>The channel this Faro follows: Canary for Faro Canary; a "Canary" setting from before it was its own app reads as Beta.</summary>
    public static string Channel => App.IsCanary ? "Canary" : FaroSettings.Current.UpdateChannel == "Stable" ? "Stable" : "Beta";

    /// <summary>A release: Version is the tag without "v" ("1.0.1-beta.3", or "canary-57" for a canary build); Name is its title.</summary>
    public sealed record Release(string Version, string Page, string? WindowsSetup, string? LinuxArchive, string Name = "");

    /// <summary>Releases from the GitHub API's JSON (tag "v0.2.4-beta.1" → version "0.2.4-beta.1").</summary>
    public static List<Release> Parse(string json) =>
        [.. (JsonNode.Parse(json) as JsonArray ?? []).OfType<JsonObject>().Where(r => r["draft"]?.GetValue<bool>() != true).Select(r =>
        {
            var assets = (r["assets"] as JsonArray ?? []).OfType<JsonObject>().Select(a => ((string?)a["name"] ?? "", (string?)a["browser_download_url"] ?? "")).ToList();
            var version = ((string?)r["tag_name"] ?? "").TrimStart('v');
            return new Release(version, (string?)r["html_url"] ?? "",
                assets.FirstOrDefault(a => a.Item1.EndsWith("-win-x64-setup.exe")).Item2 is { Length: > 0 } win ? win : null,
                assets.FirstOrDefault(a => a.Item1.EndsWith("-linux-x64.tar.gz")).Item2 is { Length: > 0 } linux ? linux : null,
                (string?)r["name"] is { Length: > 0 } name ? name : version);
        })];

    /// <summary>
    /// The newest release on the channel that is newer than <paramref name="current"/> (Canary: than build <paramref name="build"/>), if any.
    /// Stable and Beta never see canary builds (their tags aren't versions).
    /// </summary>
    public static Release? Newest(IEnumerable<Release> releases, string current, string channel, int build = 0)
    {
        static int CanaryBuild(Release r) => r.Version.StartsWith("canary-") && int.TryParse(r.Version["canary-".Length..], out var n) ? n : 0;
        if (channel == "Canary") return releases.Where(r => CanaryBuild(r) > build).MaxBy(CanaryBuild);
        var lowestStage = channel == "Beta" ? 2 : 3; // stages: dev 0, canary 1, beta 2, release 3
        var mine = ProjectSetup.VersionKey(current);
        return releases.Where(r => ProjectSetup.VersionKey(r.Version) is { } key && key.Stage >= lowestStage && (mine is null || key.CompareTo(mine.Value) > 0))
            .MaxBy(r => ProjectSetup.VersionKey(r.Version));
    }

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15), DefaultRequestHeaders = { { "User-Agent", "Faro" }, { "Accept", "application/vnd.github+json" } } };
    // The Setup is tens of MB: the API's 15 s would cut it off on a slow line (issue #25).
    static readonly HttpClient download = new() { Timeout = TimeSpan.FromMinutes(15), DefaultRequestHeaders = { { "User-Agent", "Faro" } } };

    /// <summary>Asks GitHub (unauthenticated) for the newest release on the chosen channel.</summary>
    public static async Task<Release?> Check()
    {
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=50");
        FaroSettings.Current.LastUpdateCheck = DateTime.UtcNow;
        FaroSettings.Current.Save();
        return Newest(Parse(json), App.Version, Channel, App.Build);
    }

    /// <summary>
    /// Windows: downloads the new Setup, then (once the editor agrees to close: untitled project, unsaved code) stops the running
    /// app and debugger and runs the Setup silently (it replaces the files and restarts Faro). Elsewhere: opens the release page.
    /// </summary>
    public static async Task Install(Release release, Avalonia.Controls.Window owner)
    {
        if (OperatingSystem.IsWindows() && release.WindowsSetup is { } url)
        {
            var setup = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));
            await using (var from = await download.GetStreamAsync(url))
            await using (var to = File.Create(setup))
                await from.CopyToAsync(to);
            if (owner is MainWindow editor && !await editor.ConfirmClose()) return;
            Workspace.Stop();
            Debugger.Stop();
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS") { UseShellExecute = true });
            Environment.Exit(0); // the installer replaces the files, then starts the new Faro
        }
        Open(release.Page);
    }

    /// <summary>
    /// Checks and offers the update. Automatic checks (at startup, at most daily, not for local dev builds) stay
    /// silent unless there is one; Help › Check for Updates also reports "up to date" and errors.
    /// </summary>
    public static async void Offer(Avalonia.Controls.Window owner, bool manual)
    {
        var settings = FaroSettings.Current;
        if (!manual && (!settings.CheckUpdatesOnStart || App.Version.Contains("-dev") || DateTime.UtcNow - settings.LastUpdateCheck < TimeSpan.FromDays(1))) return;
        Release? release;
        try { release = await Check(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error("Update check", e);
            if (manual) await Dialogs.Info(owner, L.T("Check for Updates"), new Avalonia.Controls.TextBlock { Text = L.T("Couldn't reach GitHub: ") + e.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            return;
        }
        if (release is null)
        {
            if (manual) await Dialogs.Info(owner, L.T("Check for Updates"), new Avalonia.Controls.TextBlock { Text = L.F("{0} {1} is up to date.", App.Name, App.DisplayVersion) });
            return;
        }
        var windows = OperatingSystem.IsWindows() && release.WindowsSetup is not null;
        if (await Dialogs.Choose(owner, L.T("Update available"), L.F("{0} is available. You have {1}.", release.Name, App.DisplayVersion)
                + (windows ? "\n\n" + L.T("Faro closes while the installer updates it, then starts again.") : ""),
                [L.T(windows ? "Install" : "Download"), L.T("Release Notes")]) is var choice and >= 0)
        {
            if (choice != 0) { Open(release.Page); return; }
            try { await Install(release, owner); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or System.ComponentModel.Win32Exception)
            {
                Log.Error("Update download", e);
                await Dialogs.Info(owner, L.T("Check for Updates"), new Avalonia.Controls.TextBlock
                {
                    Text = L.T("Couldn't download the update: ") + e.Message + "\n\n" + L.T("The release page opens so you can download it there."),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                });
                Open(release.Page);
            }
        }
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { } // no browser
    }
}
