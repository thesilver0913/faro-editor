using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>
/// Chooses a font for fontFamily: the project's own (Assets/Fonts) or one from the online catalog, which is downloaded in
/// the weights ticked and added to the project with its license (<see cref="FontLibrary"/>).
/// </summary>
public static class FontPicker
{
    sealed record Item(string Family, FontLibrary.CatalogFont? Font)
    {
        public override string ToString() => Font is null ? $"{Family}  ·  {L.T("in project")}" : Family;
    }

    static readonly string[] Categories = ["sans-serif", "serif", "display", "handwriting", "monospace"];

    /// <summary>The family chosen (already in the project), or null.</summary>
    public static async Task<string?> Show(Window owner, string root)
    {
        string? result = null;
        var closing = new CancellationTokenSource();
        CancellationTokenSource? previewing = null;
        var inProject = FontLibrary.ProjectFamilies(root);
        List<FontLibrary.CatalogFont> catalog = [];

        var search = new TextBox { PlaceholderText = L.T("Search fonts"), Width = 200 };
        var category = new ComboBox { ItemsSource = Categories.Select(L.T).Prepend(L.T("All categories")).ToList(), SelectedIndex = 0, Width = 150 };
        var list = new ListBox { Width = 360, Height = 380 };
        var preview = new TextBlock { Text = "The quick brown fox jumps over the lazy dog\n0123456789  あいうえお 永", FontSize = 26, TextWrapping = TextWrapping.Wrap, Width = 380, MinHeight = 140 };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Width = 380, Opacity = 0.7 };
        var weights = new WrapPanel { ItemSpacing = 8, Width = 380 };
        var italic = new CheckBox { Content = L.T("Italic too"), IsVisible = false };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Width = 380 };
        var progress = new ProgressBar { Maximum = 1, IsVisible = false, Width = 380 };
        var ok = new Button { Content = L.T("Use this font"), IsDefault = true, IsEnabled = false, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };

        void Filter()
        {
            var text = search.Text?.Trim() ?? "";
            bool Match(string family) => family.Contains(text, StringComparison.OrdinalIgnoreCase);
            list.ItemsSource = inProject.Where(Match).Select(f => new Item(f, null))
                .Concat(catalog.Where(f => Match(f.Family) && (category.SelectedIndex <= 0 || f.Category == Categories[category.SelectedIndex - 1])).Select(f => new Item(f.Family, f)))
                .ToList();
        }
        search.TextChanged += (_, _) => Filter();
        category.SelectionChanged += (_, _) => Filter();

        list.SelectionChanged += async (_, _) =>
        {
            previewing?.Cancel(); // a slower preview mustn't replace this one
            if (list.SelectedItem is not Item item) return;
            ok.IsEnabled = true;
            status.Text = "";
            weights.Children.Clear();
            italic.IsVisible = item.Font?.Italic == true;
            if (item.Font is not { } font)
            {
                info.Text = L.T("Already in the project (Assets/Fonts).");
                preview.FontFamily = new FontFamily(item.Family);
                return;
            }
            foreach (var weight in font.Weights)
                weights.Children.Add(new CheckBox { Content = weight.ToString(), Tag = weight, IsChecked = weight is 400 or 700 || font.Weights is [_] });
            info.Text = L.F("{0} · {1} · {2}", L.T(font.Category), font.License, font.Google ? "Google Fonts" : "Fontsource")
                + (font.Large ? "\n" + L.T("Chinese, Japanese and Korean fonts are about 5 MB per weight: tick only the weights you use.") : "");
            // The preview downloads one weight (the regular, else the nearest) into the cache
            previewing = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
            var token = previewing.Token;
            try
            {
                status.Text = L.T("Loading the preview…");
                await FontLibrary.Download(font, [(font.Weights.MinBy(w => Math.Abs(w - 400)), false)], null, token);
                FontLibrary.Use(FontLibrary.CacheOf(font));
                if (token.IsCancellationRequested) return;
                preview.FontFamily = new FontFamily(font.Family);
                status.Text = "";
            }
            catch (Exception e) when (!token.IsCancellationRequested) { status.Text = L.F("No preview: {0}", e.Message); }
            catch (OperationCanceledException) { }
        };

        var window = new Window
        {
            Title = L.T("Fonts"), SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12, Margin = new(20), Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, category } },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 16, Children =
                        {
                            list,
                            new StackPanel { Spacing = 10, Children = { preview, info, weights, italic, progress, status } },
                        },
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };

        ok.Click += async (_, _) =>
        {
            if (list.SelectedItem is not Item item) return;
            if (item.Font is { } font)
            {
                var chosen = weights.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToList();
                if (chosen.Count == 0) { status.Text = L.T("Tick at least one weight."); return; }
                var styles = chosen.Select(w => (w, false)).Concat(italic.IsVisible && italic.IsChecked == true ? chosen.Select(w => (w, true)) : []).ToList();
                ok.IsEnabled = false;
                progress.IsVisible = true;
                progress.Value = 0;
                status.Text = L.F("Downloading {0}…", font.Family);
                try
                {
                    var files = await FontLibrary.Download(font, styles, new Progress<double>(p => progress.Value = p), closing.Token);
                    FontLibrary.AddToProject(root, font, files);
                    Faro.Runtime.FaroApp.LoadFonts(root);
                }
                catch (Exception e) when (!closing.IsCancellationRequested)
                {
                    status.Text = L.F("Couldn't download the font: {0}", e.Message);
                    ok.IsEnabled = true;
                    progress.IsVisible = false;
                    return;
                }
                catch (OperationCanceledException) { return; }
            }
            result = item.Family;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Closed += (_, _) => closing.Cancel();
        window.Opened += async (_, _) =>
        {
            Filter();
            search.Focus();
            status.Text = L.T("Loading the font catalog…");
            try
            {
                catalog = await FontLibrary.Catalog(closing.Token);
                status.Text = "";
                Filter();
            }
            catch (Exception e) when (!closing.IsCancellationRequested) { status.Text = L.F("Can't load the font catalog (only the project's fonts are shown): {0}", e.Message); }
            catch (OperationCanceledException) { }
        };
        await window.ShowDialog(owner);
        return result;
    }
}
