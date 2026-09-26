using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using Faro.Runtime;

namespace Faro.Editor;

/// <summary>
/// Code editor pane (spec §8.5): AvaloniaEdit + csharp-ls over LSP for completion and error
/// underlines. Saving (Ctrl+S) diffs against the last-saved snapshot with Roslyn and rewrites
/// renamed bind targets (spec §6). Buffers and the server are static so they survive re-docking.
/// </summary>
public sealed class CodeView : UserControl
{
    static readonly Dictionary<string, TextDocument> buffers = [];
    static readonly Dictionary<string, string> saved = []; // last-saved content per file (rename detection)
    static readonly Dictionary<string, JsonArray> diagnostics = []; // by document uri
    static Task<LspClient>? lsp;
    static string lspState = "Language server: starting…";
    static int version, restarts;
    static event Action? StateChanged;

    readonly ComboBox files = new() { MinWidth = 260 };
    readonly Button save = new() { Content = "Save" };
    readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextEditor editor = new()
    {
        ShowLineNumbers = true,
        FontFamily = FontFamily.Parse("Cascadia Code,Consolas,Menlo,monospace"),
        // ponytail: built-in C# .xshd colors assume a light background; dark/TextMate themes come with spec §15
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#"),
        Background = Brushes.White,
        Foreground = Brushes.Black,
        IsEnabled = false,
    };

    public CodeView()
    {
        editor.TextArea.TextView.BackgroundRenderers.Add(new Squiggles(() => Current is { } p ? diagnostics.GetValueOrDefault(Uri(p)) : null));
        editor.TextArea.TextEntered += (_, e) => { if (e.Text == ".") Complete(); };
        editor.TextArea.Caret.PositionChanged += (_, _) => ShowStatus();
        files.SelectionChanged += (_, _) => Show();
        save.Click += (_, _) => Save();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(8), Children = { files, save, status } };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { bar, editor } };
    }

    string? Current => files.SelectedItem is string rel ? Path.Combine(Workspace.Root, rel) : null;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Refresh;
        StateChanged += Redraw;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Refresh;
        StateChanged -= Redraw;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.S) { Save(); e.Handled = true; }
        else if (command && e.Key == Key.Space) { Complete(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    /// <summary>File list refresh, plus reload of clean buffers whose file changed on disk (external edits, AI generation).</summary>
    void Refresh()
    {
        var dir = Path.Combine(Workspace.Root, "Source");
        foreach (var (path, doc) in buffers)
            if (doc.UndoStack.IsOriginalFile && File.Exists(path) && File.ReadAllText(path) is var disk && disk != saved[path])
                doc.Text = saved[path] = disk;

        var selected = files.SelectedItem as string;
        files.ItemsSource = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Workspace.Root, f)).Order().ToList()
            : [];
        files.SelectedItem = selected ?? (files.ItemsSource as List<string>)?.FirstOrDefault();
        Show();
    }

    void Show()
    {
        if (Current is not { } path || !File.Exists(path)) { editor.IsEnabled = false; return; }
        if (!buffers.TryGetValue(path, out var doc))
        {
            buffers[path] = doc = new TextDocument(saved[path] = File.ReadAllText(path));
            doc.TextChanged += (_, _) =>
            {
                WithLsp(c => c.Notify("textDocument/didChange", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = Uri(path), ["version"] = ++version },
                    ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = doc.Text }),
                }));
                StateChanged?.Invoke();
            };
            DidOpen(path, doc);
        }
        if (editor.Document != doc) editor.Document = doc;
        editor.IsEnabled = true;
        Redraw();
    }

    void Redraw()
    {
        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        save.Content = Current is { } p && buffers.TryGetValue(p, out var d) && !d.UndoStack.IsOriginalFile ? "Save *" : "Save";
        ShowStatus();
    }

    /// <summary>Status line: the diagnostic under the caret, else the language server state.</summary>
    void ShowStatus()
    {
        var caret = editor.CaretOffset;
        var here = Current is { } p && editor.Document is { } doc
            ? diagnostics.GetValueOrDefault(Uri(p))?.FirstOrDefault(d => Squiggles.Offset(doc, d!["range"]!["start"]!) <= caret && caret <= Squiggles.Offset(doc, d!["range"]!["end"]!))
            : null;
        status.Text = here is not null ? $"{here["code"]}: {here["message"]}" : lspState;
    }

    void Save()
    {
        if (Current is not { } path || !buffers.TryGetValue(path, out var doc)) return;
        var text = doc.Text;
        var renames = Registry.Renames(saved[path], text);
        File.WriteAllText(path, text);
        saved[path] = text;
        doc.UndoStack.MarkAsOriginalFile();
        if (Workspace.Project is { } project) Registry.FollowRenames(project, renames).ForEach(FaroProject.Save);
        WithLsp(c => c.Notify("textDocument/didSave", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = Uri(path) }, ["text"] = text }));
        lspState = renames.Count == 0 ? "Saved." : "Saved. Bindings followed: " + string.Join(", ", renames.Select(r => $"{r.From} → {r.To}"));
        StateChanged?.Invoke();
    }

    async void Complete()
    {
        if (Current is not { } path) return;
        var doc = editor.Document;
        var caret = editor.CaretOffset;
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(doc.GetCharAt(start - 1)) || doc.GetCharAt(start - 1) == '_')) start--;
        var location = doc.GetLocation(start);
        try
        {
            var result = await (await Lsp()).Request("textDocument/completion", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri(path) },
                ["position"] = new JsonObject { ["line"] = location.Line - 1, ["character"] = location.Column - 1 },
            });
            var items = result is JsonArray array ? array : result?["items"]?.AsArray();
            if (items is null || items.Count == 0 || editor.Document != doc || editor.CaretOffset < start) return;
            var window = new CompletionWindow(editor.TextArea) { StartOffset = start };
            foreach (var item in items)
                window.CompletionList.CompletionData.Add(new Completion((string)item!["label"]!, (string?)item["detail"]));
            window.Show();
        }
        catch (Exception e) when (e is InvalidOperationException or TaskCanceledException) { }
    }

    static string Uri(string path) => new Uri(path).AbsoluteUri;

    /// <summary>Starts (or returns) the shared language server; also called by the splash screen.</summary>
    public static Task<LspClient> Lsp() => lsp ??= StartLsp();

    static async Task<LspClient> StartLsp()
    {
        var client = await LspClient.StartAsync(Workspace.Root);
        client.Diagnostics += (uri, d) => Dispatcher.UIThread.Post(() => { diagnostics[uri] = d; StateChanged?.Invoke(); });
        client.Exited += () => Dispatcher.UIThread.Post(() =>
        {
            // ponytail: up to 3 automatic restarts per session; add a manual restart button if crashes turn out common
            lsp = null;
            lspState = ++restarts <= 3 ? "Language server crashed, restarting…" : "Language server stopped (crashed 3 times).";
            if (restarts <= 3) foreach (var (path, doc) in buffers) DidOpen(path, doc);
            StateChanged?.Invoke();
        });
        lspState = "Language server: ready";
        StateChanged?.Invoke();
        return client;
    }

    static void DidOpen(string path, TextDocument doc) => WithLsp(c => c.Notify("textDocument/didOpen", new JsonObject
    {
        ["textDocument"] = new JsonObject { ["uri"] = Uri(path), ["languageId"] = "csharp", ["version"] = ++version, ["text"] = doc.Text },
    }));

    static async void WithLsp(Action<LspClient> use)
    {
        try { use(await Lsp()); }
        catch (Exception e)
        {
            lspState = $"Language server unavailable: {e.Message}";
            StateChanged?.Invoke();
        }
    }

    sealed class Completion(string text, string? detail) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => text;
        public object Content => text;
        public object? Description => detail;
        public double Priority => 0;
        public void Complete(TextArea textArea, ISegment segment, EventArgs e) => textArea.Document.Replace(segment, text);
    }

    /// <summary>Wavy underlines for LSP diagnostics: red for errors, orange for the rest.</summary>
    sealed class Squiggles(Func<JsonArray?> current) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView view, DrawingContext dc)
        {
            if (view.Document is not { } doc || current() is not { } list) return;
            foreach (var d in list)
            {
                var start = Offset(doc, d!["range"]!["start"]!);
                var end = Math.Max(Offset(doc, d["range"]!["end"]!), Math.Min(start + 1, doc.TextLength));
                var pen = new Pen((int?)d["severity"] == 1 ? Brushes.Red : Brushes.Orange, 1);
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = start, EndOffset = end }))
                {
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(r.BottomLeft, false);
                        for (var x = r.Left + 2; x <= r.Right; x += 2) g.LineTo(new(x, r.Bottom - ((int)((x - r.Left) / 2) % 2 == 0 ? 0 : 2)));
                        g.EndFigure(false);
                    }
                    dc.DrawGeometry(null, pen, geometry);
                }
            }
        }

        /// <summary>LSP position → document offset, clamped (diagnostics may lag behind edits).</summary>
        public static int Offset(TextDocument doc, JsonNode position)
        {
            var line = doc.GetLineByNumber(Math.Clamp((int)position["line"]! + 1, 1, doc.LineCount));
            return line.Offset + Math.Min((int)position["character"]!, line.Length);
        }
    }
}
