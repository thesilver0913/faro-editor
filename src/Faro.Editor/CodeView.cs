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
using AvaloniaEdit.TextMate;
using Faro.Runtime;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

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
    static string lspState = L.T(Workspace.IsJava ? "Java: syntax colors only (no language server yet)" : "Language server: starting…");
    static int version, restarts;
    static event Action? StateChanged;
    static string? shownPath;

    /// <summary>Raised when the code history's state may have changed (edits, saves, file switch).</summary>
    public static event Action? HistoryChanged
    {
        add => StateChanged += value;
        remove => StateChanged -= value;
    }

    /// <summary>Code history (spec §10) is per file: the undo stack of the file shown in the editor,
    /// holding manual edits and approved AI changes alike.</summary>
    public static string? HistoryFile => shownPath is null ? null : Path.GetFileName(shownPath);
    public static bool CanUndo => shownPath is not null && Buffer(shownPath).UndoStack.CanUndo;
    public static bool CanRedo => shownPath is not null && Buffer(shownPath).UndoStack.CanRedo;
    public static void Undo() { if (CanUndo) Buffer(shownPath!).UndoStack.Undo(); }
    public static void Redo() { if (CanRedo) Buffer(shownPath!).UndoStack.Redo(); }

    readonly ComboBox files = new() { MinWidth = 260 };
    readonly Button save = new() { Content = L.T("Save") };
    readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextEditor editor = new()
    {
        ShowLineNumbers = true,
        FontFamily = FontFamily.Parse(App.CodeFont),
        IsEnabled = false,
    };

    public CodeView()
    {
        editor.TextArea.TextView.BackgroundRenderers.Add(new Squiggles(() => Current is { } p ? diagnostics.GetValueOrDefault(Uri(p)) : null));
        // Debugging (C#): a breakpoint gutter left of the line numbers (click: toggle, F9 too) and the stopped line highlighted.
        editor.TextArea.TextView.BackgroundRenderers.Add(new StoppedLine(() => Current));
        editor.TextArea.LeftMargins.Insert(0, new BreakpointMargin(() => Current));
        editor.TextArea.TextEntered += (_, e) => { if (e.Text == ".") Complete(); };
        editor.TextArea.Caret.PositionChanged += (_, _) => ShowStatus();
        files.SelectionChanged += (_, _) => Show();
        save.Click += (_, _) => Save();

        var bar = Icons.Toolbar(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(8), Children = { files, save, status } });
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { bar, editor } };
    }

    TextMate.Installation? textMate;

    /// <summary>Built-in C# .xshd (light) or a TextMate theme, including its editor background/foreground (spec §15).</summary>
    void ApplyTheme()
    {
        textMate?.Dispose();
        textMate = null;
        IRawTheme? theme;
        try { theme = FaroSettings.Current.LoadEditorTheme(); }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or FormatException or ArgumentException or InvalidOperationException)
        {
            lspState = L.T("Editor theme: ") + e.Message;
            theme = null;
        }
        if (theme is null)
        {
            editor.Background = Brushes.White;
            editor.Foreground = Brushes.Black;
            Highlight();
            return;
        }
        editor.SyntaxHighlighting = null;
        textMate = editor.InstallTextMate(grammars);
        Highlight();
        textMate.SetTheme(theme);
        editor.Background = textMate.TryGetThemeColor("editor.background", out var bg) && Color.TryParse(bg, out var b) ? new SolidColorBrush(b) : Brushes.White;
        editor.Foreground = textMate.TryGetThemeColor("editor.foreground", out var fg) && Color.TryParse(fg, out var f) ? new SolidColorBrush(f) : Brushes.Black;
    }

    static readonly RegistryOptions grammars = new(ThemeName.DarkPlus);

    /// <summary>Syntax colors by the shown file's extension (C#, XML, JSON, Markdown…): built-in .xshd or TextMate grammar.</summary>
    void Highlight()
    {
        var extension = Path.GetExtension(Current ?? ".cs");
        if (textMate is null) editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        else if (grammars.GetLanguageByExtension(extension) is { } language) textMate.SetGrammar(grammars.GetScopeByLanguageId(language.Id));
    }

    /// <summary>C# files get the language server, completion and rename following; other text files are plain edits.</summary>
    static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>Files the language server knows: C# in C# projects (csharp-ls), Java in Java projects (jdtls).</summary>
    static bool Served(string path) => path.EndsWith(Workspace.IsJava ? ".java" : ".cs", StringComparison.OrdinalIgnoreCase);

    string? Current => files.SelectedItem is string rel ? Path.Combine(Workspace.Root, rel) : null;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Refresh;
        StateChanged += Redraw;
        FaroSettings.ThemeChanged += ApplyTheme;
        OpenRequested += Show;
        ToggleRequested += ToggleHere;
        Debugger.Changed += RedrawStopped;
        ApplyTheme();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Refresh;
        StateChanged -= Redraw;
        FaroSettings.ThemeChanged -= ApplyTheme;
        OpenRequested -= Show;
        ToggleRequested -= ToggleHere;
        Debugger.Changed -= RedrawStopped;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.Space) { Complete(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    /// <summary>File list refresh, plus reload of clean buffers whose file changed on disk (external edits, AI generation).</summary>
    void Refresh()
    {
        var dir = Path.Combine(Workspace.Root, "Source");
        foreach (var (path, doc) in buffers)
            if (doc.UndoStack.IsOriginalFile && File.Exists(path) && File.ReadAllText(path) is var disk && disk != saved[path])
            {
                doc.Text = saved[path] = disk;
                doc.UndoStack.MarkAsOriginalFile(); // the reload is the file's content now, not an unsaved edit
            }

        var selected = files.SelectedItem as string;
        // Source/*.cs (*.java), plus any other text file opened from the explorer.
        files.ItemsSource = (Directory.Exists(dir) ? Directory.EnumerateFiles(dir, Workspace.IsJava ? "*.java" : "*.cs", SearchOption.AllDirectories) : [])
            .Concat(buffers.Keys.Where(File.Exists)).Select(f => Path.GetRelativePath(Workspace.Root, f)).Distinct().Order().ToList();
        files.SelectedItem = selected ?? (files.ItemsSource as List<string>)?.FirstOrDefault();
        Show();
    }

    void ToggleHere() { if (Current is { } path) Debugger.Toggle(path, editor.TextArea.Caret.Line); }

    void RedrawStopped() => editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

    void Show(string path, int line, int column)
    {
        Buffer(path); // listed from now on, even outside Source/
        Refresh(); // a new file may not be listed yet
        files.SelectedItem = Path.GetRelativePath(Workspace.Root, path);
        if (line <= 0 || editor.Document is not { } doc || line > doc.LineCount) return;
        editor.TextArea.Caret.Position = new(line, Math.Max(column, 1));
        editor.ScrollTo(line, column);
        editor.Focus();
    }

    void Show()
    {
        if (Current is not { } path || !File.Exists(path)) { editor.IsEnabled = false; return; }
        var doc = Buffer(path);
        if (editor.Document != doc) editor.Document = doc;
        if (shownPath != path) { shownPath = path; Highlight(); StateChanged?.Invoke(); }
        editor.IsEnabled = true;
        Redraw();
    }

    void Redraw()
    {
        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        save.Content = Current is { } p && buffers.TryGetValue(p, out var d) && !d.UndoStack.IsOriginalFile ? L.T("Save") + " *" : L.T("Save");
        ShowStatus();
    }

    /// <summary>Status line: the diagnostic under the caret, else the language server state.</summary>
    void ShowStatus()
    {
        var caret = editor.CaretOffset;
        var here = Current is { } p && editor.Document is { } doc
            ? diagnostics.GetValueOrDefault(Uri(p))?.FirstOrDefault(d => Squiggles.Offset(doc, d!["range"]!["start"]!) <= caret && caret <= Squiggles.Offset(doc, d!["range"]!["end"]!))
            : null;
        status.Text = here is not null ? (here["code"]?.ToString() is { } code && !code.All(char.IsDigit) ? $"{code}: " : "") + here["message"] // jdtls codes are bare numbers
            : Current is { } path && IsDirty(path) && ChangedOnDisk(path) ? L.T("Changed on disk too: saving will ask before overwriting.") : lspState;
    }

    async void Save()
    {
        if (Current is { } path && buffers.ContainsKey(path) && await ConfirmOverwrite((Window)TopLevel.GetTopLevel(this)!, [path])) SaveBuffer(path);
    }

    /// <summary>The shared buffer for a file (created on first use; empty for a file that doesn't exist yet).</summary>
    static TextDocument Buffer(string path)
    {
        if (buffers.TryGetValue(path, out var doc)) return doc;
        buffers[path] = doc = new TextDocument(saved[path] = File.Exists(path) ? File.ReadAllText(path) : "");
        doc.TextChanged += (_, _) =>
        {
            if (Served(path)) WithLsp(c => c.Notify("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri(path), ["version"] = ++version },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = doc.Text }),
            }));
            StateChanged?.Invoke();
        };
        if (Served(path)) DidOpen(path, doc);
        return doc;
    }

    static event Action<string, int, int>? OpenRequested;

    /// <summary>Shows a Source/ file in the code editor (from the explorer), optionally at a 1-based line/column (build errors).</summary>
    public static void Open(string path, int line = 0, int column = 0) => OpenRequested?.Invoke(path, line, column);

    /// <summary>Drops the buffer of a file that was renamed or deleted (it has no unsaved edits by then).</summary>
    public static void Forget(string path)
    {
        foreach (var key in buffers.Keys.Where(k => k == path || k.StartsWith(path + Path.DirectorySeparatorChar)).ToList())
        {
            buffers.Remove(key);
            saved.Remove(key);
        }
        StateChanged?.Invoke();
    }

    public static bool AnyDirty => buffers.Values.Any(d => !d.UndoStack.IsOriginalFile);

    /// <summary>Saves every edited file; asks first when that would overwrite a change made outside Faro.</summary>
    public static async Task SaveAll(Window owner)
    {
        var dirty = buffers.Keys.Where(IsDirty).ToList();
        if (await ConfirmOverwrite(owner, dirty)) dirty.ForEach(SaveBuffer);
    }

    /// <summary>The file changed on disk since Faro last loaded or saved it (another editor, git).</summary>
    static bool ChangedOnDisk(string path) => File.Exists(path) && File.ReadAllText(path) != saved[path];

    static async Task<bool> ConfirmOverwrite(Window owner, List<string> paths) =>
        paths.Where(ChangedOnDisk).Select(Path.GetFileName).ToList() is not { Count: > 0 } changed
        || await Dialogs.Confirm(owner, L.T("Changed on disk"), L.F("{0} changed outside Faro since it was opened. Saving overwrites those changes.", string.Join(", ", changed)), L.T("Overwrite"));

    public static bool IsDirty(string path) => buffers.TryGetValue(path, out var doc) && !doc.UndoStack.IsOriginalFile;

    /// <summary>
    /// Applies approved AI output through the editor buffer, so it lands in the same undo history as manual
    /// edits (spec §10), shows up in the editor at once, and gets the same rename follow on save (spec §6).
    /// </summary>
    public static void ApplyGenerated(string path, string text)
    {
        Buffer(path).Text = text;
        SaveBuffer(path);
    }

    static void SaveBuffer(string path)
    {
        var doc = buffers[path];
        var text = doc.Text;
        var renames = IsCSharp(path) ? Registry.Renames(saved[path], text) : [];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        saved[path] = text;
        doc.UndoStack.MarkAsOriginalFile();
        if (Workspace.Project is { } project) Registry.FollowRenames(project, renames).ForEach(FaroProject.Save);
        if (Served(path)) WithLsp(c => c.Notify("textDocument/didSave", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = Uri(path) }, ["text"] = text }));
        lspState = renames.Count == 0 ? $"Saved {Path.GetFileName(path)}." : "Saved. Bindings followed: " + string.Join(", ", renames.Select(r => $"{r.From} → {r.To}"));
        StateChanged?.Invoke();
    }

    async void Complete()
    {
        if (Current is not { } path || !Served(path)) return;
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
    public static Task<LspClient> Lsp() => Workspace.Trusted ? lsp ??= StartLsp()
        : Task.FromException<LspClient>(new InvalidOperationException("off in restricted mode (File › Trust Project…)"));

    static async Task<LspClient> StartLsp()
    {
        var client = await LspClient.StartAsync(Workspace.Root);
        client.Diagnostics += (uri, d) => Dispatcher.UIThread.Post(() => { diagnostics[uri] = d; StateChanged?.Invoke(); });
        client.Exited += () => Dispatcher.UIThread.Post(() =>
        {
            // ponytail: up to 3 automatic restarts per session; add a manual restart button if crashes turn out common
            lsp = null;
            lspState = L.T(++restarts <= 3 ? "Language server crashed, restarting…" : "Language server stopped (crashed 3 times).");
            if (restarts <= 3) foreach (var (path, doc) in buffers.Where(b => Served(b.Key))) DidOpen(path, doc);
            StateChanged?.Invoke();
        });
        lspState = L.T("Language server: ready");
        StateChanged?.Invoke();
        return client;
    }

    static void DidOpen(string path, TextDocument doc) => WithLsp(c => c.Notify("textDocument/didOpen", new JsonObject
    {
        ["textDocument"] = new JsonObject { ["uri"] = Uri(path), ["languageId"] = Workspace.IsJava ? "java" : "csharp", ["version"] = ++version, ["text"] = doc.Text },
    }));

    static async void WithLsp(Action<LspClient> use)
    {
        try { use(await Lsp()); }
        catch (Exception e)
        {
            lspState = L.T("Language server unavailable: ") + e.Message;
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
    /// <summary>F9: toggles a breakpoint on the caret's line.</summary>
    public static void ToggleBreakpoint() => ToggleRequested?.Invoke();
    static event Action? ToggleRequested;

    sealed class StoppedLine(Func<string?> current) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView view, DrawingContext dc)
        {
            if (Debugger.Stopped is not { } at || at.Path != current() || view.Document is not { } doc || at.Line < 1 || at.Line > doc.LineCount) return;
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(view, doc.GetLineByNumber(at.Line), true))
                dc.FillRectangle(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD0, 0x00)), new Avalonia.Rect(0, r.Top, view.Bounds.Width, r.Height));
        }
    }

    sealed class BreakpointMargin : AbstractMargin
    {
        readonly Func<string?> current;

        public BreakpointMargin(Func<string?> current)
        {
            this.current = current;
            Cursor = new Cursor(StandardCursorType.Hand);
            Debugger.Changed += InvalidateVisual;
        }

        protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize) => new(16, 0);

        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null) oldTextView.VisualLinesChanged -= Redraw;
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null) newTextView.VisualLinesChanged += Redraw;
        }

        void Redraw(object? sender, EventArgs e) => InvalidateVisual();

        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(Brushes.Transparent, new Avalonia.Rect(Bounds.Size)); // clickable everywhere
            if (TextView is not { VisualLinesValid: true } view || current() is not { } path || !Debugger.Breakpoints.TryGetValue(path, out var lines)) return;
            foreach (var line in view.VisualLines.Where(l => lines.Contains(l.FirstDocumentLine.LineNumber)))
                dc.DrawEllipse(Brushes.IndianRed, null, new Avalonia.Point(8, line.VisualTop - view.VerticalOffset + line.Height / 2), 5, 5);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (TextView is not { } view || current() is not { } path) return;
            if (view.GetVisualLineFromVisualTop(e.GetPosition(view).Y + view.VerticalOffset) is { } line) Debugger.Toggle(path, line.FirstDocumentLine.LineNumber);
            e.Handled = true;
        }
    }

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
