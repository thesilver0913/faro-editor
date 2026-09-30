using Avalonia.Controls;
using Avalonia.Layout;

namespace Faro.Editor;

/// <summary>
/// Material Symbols (Google, Apache License 2.0) fetched one at a time, like fonts: the names from the icon repository's
/// codepoints list (cached for a week), each symbol's SVG from fonts.gstatic.com, cached as &lt;cache&gt;/Assets/Icons/&lt;name&gt;.svg
/// (so FaroIcon previews it with the cache as its root) and copied into the project's Assets/Icons with the license.
/// The runtimes draw Assets/Icons/&lt;name&gt;.svg for a name not in the bundled IconSet.
/// </summary>
public static partial class IconLibrary
{
    const string CatalogUrl = "https://raw.githubusercontent.com/google/material-design-icons/master/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.codepoints";
    public static string SvgUrl(string name) => $"https://fonts.gstatic.com/s/i/short-term/release/materialsymbolsoutlined/{name}/default/24px.svg";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string CacheRoot => Path.Combine(Path.GetDirectoryName(FaroSettings.FilePath)!, "icons");

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9_]+$")]
    private static partial System.Text.RegularExpressions.Regex NamePattern();

    /// <summary>"home e88a" lines → names (anything else is skipped).</summary>
    public static List<string> ParseCatalog(string text) =>
        [.. text.Split('\n').Select(l => l.Split(' ')[0].Trim()).Where(n => NamePattern().IsMatch(n)).Distinct()];

    /// <summary>The names, fetched again after a week; offline, the last list (none yet: the error).</summary>
    public static async Task<List<string>> Catalog(CancellationToken cancel)
    {
        var file = Path.Combine(CacheRoot, "catalog.txt");
        if (!File.Exists(file) || File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
            try
            {
                var text = await http.GetStringAsync(CatalogUrl, cancel);
                if (ParseCatalog(text).Count == 0) throw new InvalidDataException("empty icon list"); // a broken answer doesn't replace a good cache
                Directory.CreateDirectory(CacheRoot);
                await File.WriteAllTextAsync(file, text, cancel);
            }
            catch (Exception) when (File.Exists(file) && !cancel.IsCancellationRequested) { }
        return ParseCatalog(await File.ReadAllTextAsync(file, cancel));
    }

    /// <summary>The symbol's SVG in the cache (downloaded when missing); throws when it isn't one.</summary>
    public static async Task<string> Fetch(string name, CancellationToken cancel)
    {
        if (!NamePattern().IsMatch(name)) throw new ArgumentException($"'{name}' is not an icon name.");
        var file = Path.Combine(CacheRoot, "Assets", "Icons", name + ".svg");
        if (File.Exists(file)) return file;
        var svg = await http.GetStringAsync(SvgUrl(name), cancel);
        if (svg.Length > 64 * 1024 || !svg.TrimStart().StartsWith("<svg") || Faro.Runtime.IconSet.Svg(svg) is null)
            throw new InvalidDataException(L.T("The download is not an icon."));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, svg, cancel);
        return file;
    }

    /// <summary>Copies a fetched symbol into Assets/Icons, with the license once.</summary>
    public static void AddToProject(string root, string name, string file)
    {
        var dir = Path.Combine(root, "Assets", "Icons");
        Directory.CreateDirectory(dir);
        File.Copy(file, Path.Combine(dir, name + ".svg"), true);
        var license = Path.Combine(dir, "LICENSE.txt");
        var apache = Path.Combine(AppContext.BaseDirectory, "licenses", "Apache-2.0.txt");
        if (!File.Exists(license))
            File.WriteAllText(license, "Material Symbols, Copyright Google LLC (https://fonts.google.com/icons)\n\n" + (File.Exists(apache) ? File.ReadAllText(apache) : "Apache License 2.0\n"));
    }

    /// <summary>The symbols already in the project (Assets/Icons/*.svg).</summary>
    public static List<string> ProjectIcons(string root) =>
        Directory.Exists(Path.Combine(root, "Assets", "Icons"))
            ? [.. Directory.EnumerateFiles(Path.Combine(root, "Assets", "Icons"), "*.svg").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()]
            : [];

    /// <summary>Search the online symbols, preview one, and add it to the project; the name chosen, or null.</summary>
    public static async Task<string?> Pick(Window owner, string root)
    {
        string? result = null;
        var closing = new CancellationTokenSource();
        List<string> catalog = [];
        var search = new TextBox { PlaceholderText = L.T("Search icons (English, e.g. wallet)"), Width = 320 };
        var list = new ListBox { Width = 320, Height = 360 };
        var preview = new Faro.Runtime.FaroIcon { Width = 96, Height = 96, Root = CacheRoot };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Width = 220 };
        var ok = new Button { Content = L.T("Use this icon"), IsDefault = true, IsEnabled = false, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
        void Filter()
        {
            var text = search.Text?.Trim().Replace(' ', '_').ToLowerInvariant() ?? "";
            list.ItemsSource = catalog.Where(n => n.Contains(text)).OrderBy(n => !n.StartsWith(text)).Take(300).ToList(); // ponytail: first 300 matches
        }
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is not string name) return;
            ok.IsEnabled = false;
            status.Text = L.T("Loading the preview…");
            try
            {
                await Fetch(name, closing.Token);
                if (list.SelectedItem as string != name) return; // another one was chosen meanwhile
                preview.Icon = null;
                preview.Icon = name;
                status.Text = name;
                ok.IsEnabled = true;
            }
            catch (Exception e) when (!closing.IsCancellationRequested) { status.Text = L.F("No preview: {0}", e.Message); }
        };
        var window = new Window
        {
            Title = L.T("Icons"), SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12, Margin = new(20), Children =
                {
                    search,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { list, new StackPanel { Spacing = 10, Children = { preview, status } } } },
                    new TextBlock { Text = L.T("Material Symbols (Google, Apache License 2.0). The icon is copied into Assets/Icons with its license."), Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 560 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += async (_, _) =>
        {
            if (list.SelectedItem is not string name) return;
            AddToProject(root, name, await Fetch(name, closing.Token));
            result = name;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Closed += (_, _) => closing.Cancel();
        window.Opened += async (_, _) =>
        {
            status.Text = L.T("Loading the icon list…");
            try { catalog = await Catalog(closing.Token); status.Text = L.F("{0} icons", catalog.Count); Filter(); }
            catch (Exception e) when (!closing.IsCancellationRequested) { status.Text = L.F("Couldn't load the icon list: {0}", e.Message); }
        };
        await window.ShowDialog(owner);
        return result;
    }
}
