using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Faro.Editor;

/// <summary>
/// Update check against GitHub Releases. Channels (Preferences): Stable = releases (main), Beta = + "-beta.N",
/// Canary = + "-canary.N" pre-releases. Windows installs by running the new Setup (Inno Setup upgrades in place and
/// restarts Faro); on Linux the release page opens for the archive. Only reads public release data; sends nothing.
/// </summary>
public static class Updates
{
    public const string Repository = "thesilver0913/faro-editor";
    public static readonly string[] Channels = ["Stable", "Beta", "Canary"];

    public sealed record Release(string Version, string Page, string? WindowsSetup, string? LinuxArchive);

    /// <summary>Releases from the GitHub API's JSON (tag "v0.2.4-beta.1" → version "0.2.4-beta.1").</summary>
    public static List<Release> Parse(string json) =>
        [.. (JsonNode.Parse(json) as JsonArray ?? []).OfType<JsonObject>().Where(r => r["draft"]?.GetValue<bool>() != true).Select(r =>
        {
            var assets = (r["assets"] as JsonArray ?? []).OfType<JsonObject>().Select(a => ((string?)a["name"] ?? "", (string?)a["browser_download_url"] ?? "")).ToList();
            return new Release(((string?)r["tag_name"] ?? "").TrimStart('v'), (string?)r["html_url"] ?? "",
                assets.FirstOrDefault(a => a.Item1.EndsWith("-win-x64-setup.exe")).Item2 is { Length: > 0 } win ? win : null,
                assets.FirstOrDefault(a => a.Item1.EndsWith("-linux-x64.tar.gz")).Item2 is { Length: > 0 } linux ? linux : null);
        })];

    /// <summary>The newest release on the channel that is newer than <paramref name="current"/>, if any.</summary>
    public static Release? Newest(IEnumerable<Release> releases, string current, string channel)
    {
        var lowestStage = channel switch { "Canary" => 1, "Beta" => 2, _ => 3 }; // stages: dev 0, canary 1, beta 2, release 3
        var mine = ProjectSetup.VersionKey(current);
        return releases.Where(r => ProjectSetup.VersionKey(r.Version) is { } key && key.Stage >= lowestStage && (mine is null || key.CompareTo(mine.Value) > 0))
            .MaxBy(r => ProjectSetup.VersionKey(r.Version));
    }

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15), DefaultRequestHeaders = { { "User-Agent", "Faro" }, { "Accept", "application/vnd.github+json" } } };

    /// <summary>Asks GitHub (unauthenticated) for the newest release on the chosen channel.</summary>
    public static async Task<Release?> Check()
    {
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=50");
        FaroSettings.Current.LastUpdateCheck = DateTime.UtcNow;
        FaroSettings.Current.Save();
        return Newest(Parse(json), App.Version, FaroSettings.Current.UpdateChannel);
    }

    /// <summary>Windows: downloads and runs the new Setup silently (it closes and restarts Faro). Elsewhere: opens the release page.</summary>
    public static async Task Install(Release release)
    {
        if (OperatingSystem.IsWindows() && release.WindowsSetup is { } url)
        {
            var setup = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));
            await File.WriteAllBytesAsync(setup, await http.GetByteArrayAsync(url));
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
            if (manual) await Dialogs.Info(owner, L.T("Check for Updates"), new Avalonia.Controls.TextBlock { Text = L.F("Faro {0} is up to date ({1}).", App.Version, L.T(settings.UpdateChannel)) });
            return;
        }
        var windows = OperatingSystem.IsWindows() && release.WindowsSetup is not null;
        if (await Dialogs.Choose(owner, L.T("Update available"), L.F("Faro {0} is available ({1}). You have {2}.", release.Version, L.T(settings.UpdateChannel), App.Version)
                + (windows ? "\n\n" + L.T("Faro closes while the installer updates it, then starts again.") : ""),
                [L.T(windows ? "Install" : "Download"), L.T("Release Notes")]) is var choice and >= 0)
        {
            if (choice == 0) await Install(release); else Open(release.Page);
        }
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { } // no browser
    }
}
