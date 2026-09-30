using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Faro.Editor;

/// <summary>
/// Online fonts, like Adobe Fonts: the editor ships none. The catalog comes from Fontsource (Google Fonts and other open
/// fonts, no API key). Google fonts download as whole TTFs per weight from the Google Fonts CSS API (a client without a
/// browser User-Agent gets TrueType, not split WOFF2); the others from Fontsource's CDN (their default subset). Files are
/// cached next to the settings, then copied into the project's Assets/Fonts with the license, where both runtimes load them.
/// </summary>
public static class FontLibrary
{
    public sealed record CatalogFont(string Id, string Family, string Category, int[] Weights, bool Italic, string Subset, string License, bool Google)
    {
        /// <summary>Chinese, Japanese and Korean fonts are several MB per weight.</summary>
        public bool Large => Subset is "japanese" or "korean" || Subset.StartsWith("chinese");
    }

    const long MaxBytes = 50 << 20; // one font file; the largest CJK weights are ~20 MB
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) }; // no User-Agent on purpose, see above

    static string CacheDir => Path.Combine(Path.GetDirectoryName(FaroSettings.FilePath)!, "fonts");

    /// <summary>Fontsource's list (api.fontsource.org/v1/fonts).</summary>
    public static List<CatalogFont> ParseCatalog(string json) => JsonNode.Parse(json)!.AsArray().Select(f => new CatalogFont(
        (string)f!["id"]!, (string)f["family"]!, (string?)f["category"] ?? "",
        f["weights"]?.AsArray().Select(w => (int)w!).Order().ToArray() ?? [400],
        f["styles"]?.AsArray().Any(s => (string?)s == "italic") == true,
        (string?)f["defSubset"] ?? "latin", (string?)f["license"] ?? "", (string?)f["type"] == "google")).ToList();

    /// <summary>The catalog, fetched again after a week; offline, the last one (none yet: the error).</summary>
    public static async Task<List<CatalogFont>> Catalog(CancellationToken cancel)
    {
        var file = Path.Combine(CacheDir, "catalog.json");
        if (!File.Exists(file) || File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
            try
            {
                var json = await http.GetStringAsync("https://api.fontsource.org/v1/fonts", cancel);
                ParseCatalog(json); // a broken answer doesn't replace a good cache
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllTextAsync(file, json, cancel);
            }
            catch (Exception) when (File.Exists(file) && !cancel.IsCancellationRequested) { }
        return ParseCatalog(await File.ReadAllTextAsync(file, cancel));
    }

    /// <summary>Google Fonts CSS API v2 for the styles (its tuples sorted: upright first, then by weight).</summary>
    public static string GoogleCssUrl(string family, IEnumerable<(int Weight, bool Italic)> styles) =>
        "https://fonts.googleapis.com/css2?family=" + Uri.EscapeDataString(family).Replace("%20", "+") + ":ital,wght@"
        + string.Join(";", styles.OrderBy(s => s.Italic).ThenBy(s => s.Weight).Select(s => $"{(s.Italic ? 1 : 0)},{s.Weight}"));

    /// <summary>The @font-face rules of a Google Fonts stylesheet: each style's file.</summary>
    public static List<(int Weight, bool Italic, string Url)> ParseGoogleCss(string css) =>
        Regex.Matches(css, @"@font-face\s*\{([^}]*)\}").Select(m => m.Groups[1].Value).Select(rule => (
            int.Parse(Regex.Match(rule, @"font-weight:\s*(\d+)").Groups[1].Value),
            Regex.IsMatch(rule, @"font-style:\s*italic"),
            Regex.Match(rule, @"url\(\s*['""]?([^)'""]+)").Groups[1].Value)).ToList();

    public static string FontsourceUrl(CatalogFont font, int weight, bool italic) =>
        $"https://cdn.jsdelivr.net/fontsource/fonts/{font.Id}@latest/{font.Subset}-{weight}-{(italic ? "italic" : "normal")}.ttf";

    public static string FileName(string family, int weight, bool italic) => $"{family.Replace(" ", "")}-{weight}{(italic ? "Italic" : "")}.ttf";

    /// <summary>The styles' files in the cache, downloading the missing ones (progress: 0–1).</summary>
    public static async Task<List<string>> Download(CatalogFont font, IReadOnlyList<(int Weight, bool Italic)> styles, IProgress<double>? progress, CancellationToken cancel)
    {
        var dir = Path.Combine(CacheDir, font.Id);
        string FileOf((int Weight, bool Italic) s) => Path.Combine(dir, FileName(font.Family, s.Weight, s.Italic));
        var missing = styles.Where(s => !File.Exists(FileOf(s))).ToList();
        if (missing.Count > 0)
        {
            var urls = font.Google
                ? ParseGoogleCss(await http.GetStringAsync(GoogleCssUrl(font.Family, missing), cancel)).ToDictionary(f => (f.Weight, f.Italic), f => f.Url)
                : missing.ToDictionary(s => s, s => FontsourceUrl(font, s.Weight, s.Italic));
            Directory.CreateDirectory(dir);
            for (var i = 0; i < missing.Count; i++)
            {
                if (!urls.TryGetValue(missing[i], out var url)) throw new InvalidDataException(L.F("{0} has no {1} style.", font.Family, missing[i].Weight));
                var bytes = await Fetch(url, cancel);
                var temp = FileOf(missing[i]) + ".part"; // never a half-written font in the cache
                await File.WriteAllBytesAsync(temp, bytes, cancel);
                File.Move(temp, FileOf(missing[i]), true);
                progress?.Report((i + 1.0) / missing.Count);
            }
        }
        return styles.Select(FileOf).ToList();
    }

    static async Task<byte[]> Fetch(string url, CancellationToken cancel)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var bytes = new MemoryStream();
        var buffer = new byte[81920];
        for (int read; (read = await stream.ReadAsync(buffer, cancel)) > 0; )
            if (bytes.Length + read > MaxBytes) throw new InvalidDataException(L.T("The font file is too large."));
            else bytes.Write(buffer, 0, read);
        return IsFont(bytes.ToArray()) ? bytes.ToArray() : throw new InvalidDataException(L.T("The download is not a font file."));
    }

    /// <summary>TrueType or OpenType, by the first four bytes.</summary>
    public static bool IsFont(byte[] bytes) => bytes.Length > 12 && Encoding.ASCII.GetString(bytes, 0, 4) is "\0\u0001\0\0" or "OTTO" or "true";

    /// <summary>The family (typographic, else legacy) and copyright from a font's name table; null when unreadable.</summary>
    public static (string Family, string Copyright)? Names(byte[] font)
    {
        int U16(int at) => font[at] << 8 | font[at + 1];
        int U32(int at) => U16(at) << 16 | U16(at + 2);
        try
        {
            var table = Enumerable.Range(0, U16(4)).Select(i => 12 + i * 16).FirstOrDefault(at => Encoding.ASCII.GetString(font, at, 4) == "name");
            if (table == 0) return null;
            var start = U32(table + 8);
            var strings = start + U16(start + 4);
            var names = new Dictionary<int, string>();
            for (var i = 0; i < U16(start + 2); i++)
            {
                var at = start + 6 + i * 12;
                var (platform, language, id) = (U16(at), U16(at + 4), U16(at + 6));
                var text = font.AsSpan(strings + U16(at + 10), U16(at + 8));
                if (platform == 3 && language == 0x409) names[id] = Encoding.BigEndianUnicode.GetString(text); // Windows, English
                else if (platform == 1 && language == 0) names.TryAdd(id, Encoding.Latin1.GetString(text)); // Mac, English
            }
            return names.TryGetValue(16, out var family) || names.TryGetValue(1, out family) ? (family, names.GetValueOrDefault(0, "")) : null;
        }
        catch (ArgumentException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    /// <summary>The families in the project's Assets/Fonts.</summary>
    public static List<string> ProjectFamilies(string root)
    {
        var dir = Path.Combine(root, "Assets", "Fonts");
        return !Directory.Exists(dir) ? [] : Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .Select(f => Names(File.ReadAllBytes(f))?.Family).OfType<string>().Distinct().Order().ToList();
    }

    /// <summary>Copies downloaded files into Assets/Fonts with {Family}-LICENSE.txt (copyright plus the license text).</summary>
    public static void AddToProject(string root, CatalogFont font, IEnumerable<string> files)
    {
        var dir = Path.Combine(root, "Assets", "Fonts");
        Directory.CreateDirectory(dir);
        var copyright = "";
        foreach (var file in files)
        {
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
            if (copyright.Length == 0) copyright = Names(File.ReadAllBytes(file))?.Copyright ?? "";
        }
        var license = font.License.StartsWith("OFL") ? "OFL-1.1.txt" : font.License.StartsWith("Apache") ? "Apache-2.0.txt" : null;
        var text = license is not null && File.Exists(Path.Combine(AppContext.BaseDirectory, "licenses", license))
            ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "licenses", license))
            : $"License: {font.License} (https://fontsource.org/fonts/{font.Id})\n";
        File.WriteAllText(Path.Combine(dir, font.Family.Replace(" ", "") + "-LICENSE.txt"), $"{font.Family}\n{copyright}\n\n{text}");
    }

    /// <summary>Lets the editor draw with fonts in a folder (the cache's, for previews).</summary>
    public static void Use(string dir)
    {
        if (Directory.Exists(dir) && Avalonia.Media.FontManager.Current.SystemFonts is Avalonia.Media.Fonts.FontCollectionBase fonts)
            fonts.TryAddFontSource(new Uri(Path.GetFullPath(dir) + Path.DirectorySeparatorChar));
    }

    public static string CacheOf(CatalogFont font) => Path.Combine(CacheDir, font.Id);
}
