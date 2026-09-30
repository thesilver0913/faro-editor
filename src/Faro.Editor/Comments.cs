using System.Xml;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>
/// Review comments (Figma's comments, shared through Git instead of a server): Comments/&lt;screenId&gt;.xml next to UI/,
/// one &lt;Comment node author date resolved text&gt; per thread with its &lt;Reply author date text&gt;s. The app never
/// packs Comments/. Edits are UI history steps (undoable); a renamed node takes its comments along (CanvasEdit.Rename),
/// and a deleted node's comments stay, shown on the screen.
/// </summary>
public static class Comments
{
    public static string PathFor(string root, string screen) => Path.Combine(root, "Comments", screen + ".xml");

    /// <summary>A screen's comments; null when it has none (or the file isn't readable).</summary>
    public static XDocument? Load(string root, string screen)
    {
        var path = PathFor(root, screen);
        try { return File.Exists(path) ? XDocument.Load(path, LoadOptions.SetBaseUri) : null; }
        catch (XmlException) { return null; }
    }

    /// <summary>Every comment thread in the project: (screen, the Comment element).</summary>
    public static List<(string Screen, XElement Comment)> All(string root) =>
        !Directory.Exists(Path.Combine(root, "Comments")) ? [] :
        [.. Directory.EnumerateFiles(Path.Combine(root, "Comments"), "*.xml").Order()
            .SelectMany(f => Load(root, Path.GetFileNameWithoutExtension(f))?.Root?.Elements("Comment").Select(c => (Path.GetFileNameWithoutExtension(f), c)) ?? [])];

    /// <summary>Who writes: git's user.name for the project (as commits show it), else the OS user.</summary>
    public static string Author => author ??= GitUserName() ?? Environment.UserName;
    static string? author;

    static string? GitUserName()
    {
        try
        {
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", ["-C", Workspace.Root, "config", "user.name"]) { RedirectStandardOutput = true, CreateNoWindow = true });
            var name = git?.StandardOutput.ReadToEnd().Trim();
            git?.WaitForExit();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (System.ComponentModel.Win32Exception) { return null; } // no git
    }

    static XElement Entry(string name, string text, params XAttribute[] first) =>
        new(name, first, new XAttribute("author", Author), new XAttribute("date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")), new XAttribute("text", text.Trim()));

    /// <summary>Adds a thread on a node ("" = the screen itself).</summary>
    public static void Add(string root, string screen, string node, string text)
    {
        if (text.Trim().Length == 0) return;
        var path = PathFor(root, screen);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Comments>\n</Comments>\n"); // ponytail: stays behind (empty) if the step is undone; harmless
        }
        var doc = Load(root, screen)!;
        doc.Root!.Add(Entry("Comment", text, new XAttribute("node", node)));
        Commit("Add comment", doc);
    }

    public static void Reply(XElement comment, string text)
    {
        if (text.Trim().Length == 0) return;
        comment.Add(Entry("Reply", text));
        Commit("Reply to comment", comment.Document!);
    }

    public static void SetResolved(XElement comment, bool resolved)
    {
        comment.SetAttributeValue("resolved", resolved ? "true" : null);
        Commit(resolved ? "Resolve comment" : "Reopen comment", comment.Document!);
    }

    public static void Delete(XElement comment)
    {
        var doc = comment.Document!;
        comment.Remove();
        Commit("Delete comment", doc);
    }

    public static bool IsResolved(XElement comment) => (string?)comment.Attribute("resolved") == "true";

    static void Commit(string label, XDocument doc)
    {
        UiHistory.Commit(label, [doc]);
        Workspace.Reload();
    }

    /// <summary>Asks the Comments tab to show a thread (a canvas pin was clicked).</summary>
    public static event Action<string, string>? Reveal;
    public static void Show(string screen, string node)
    {
        ConsoleView.Show(ConsoleView.Tab.Comments);
        Reveal?.Invoke(screen, node);
    }
}

/// <summary>Console › Comments: every thread (unresolved only by default), newest reply last; a click opens its node.</summary>
public sealed class CommentsView : UserControl
{
    readonly StackPanel list = new() { Spacing = 10, Margin = new(8) };
    readonly CheckBox unresolvedOnly = new() { Content = L.T("Unresolved only"), IsChecked = true };
    readonly TextBox draft = new() { PlaceholderText = L.T("Comment on the selected node (or the screen)… Enter"), MinWidth = 260 };
    (string Screen, string Node)? highlight;

    public CommentsView()
    {
        unresolvedOnly.IsCheckedChanged += (_, _) => Build();
        draft.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || CanvasView.CurrentScreen is not { } screen || Workspace.Project is null) return;
            var node = CanvasView.Selection.Count == 1 ? CanvasView.Selection.First() : "";
            Comments.Add(Workspace.Root, screen, node, draft.Text ?? "");
            draft.Text = "";
        };
        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 4, Margin = new(8, 6), Children = { draft, unresolvedOnly } };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { bar, new ScrollViewer { Content = list } } };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Workspace.Changed += Build;
        Comments.Reveal += Focus;
        Build();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Workspace.Changed -= Build;
        Comments.Reveal -= Focus;
        base.OnDetachedFromVisualTree(e);
    }

    void Focus(string screen, string node)
    {
        highlight = (screen, node);
        Build();
    }

    void Build()
    {
        list.Children.Clear();
        var threads = Comments.All(Workspace.Root).Where(t => unresolvedOnly.IsChecked != true || !Comments.IsResolved(t.Comment)).ToList();
        if (threads.Count == 0)
            list.Children.Add(new TextBlock { Text = L.T("No comments. Select a node, type above and press Enter; they're saved in Comments/ for Git."), Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        foreach (var (screen, comment) in threads) list.Children.Add(Thread(screen, comment));
    }

    Control Thread(string screen, XElement comment)
    {
        var node = (string?)comment.Attribute("node") ?? "";
        var exists = node.Length == 0 || Workspace.Project?.Graph(screen)?.Descendants("Node").Any(n => (string?)n.Attribute("id") == node) == true;
        var where = new Button
        {
            Content = $"{screen} › {(node.Length == 0 ? L.T("(screen)") : exists ? node : L.F("{0} (removed)", node))}",
            Padding = new(6, 2), FontWeight = FontWeight.SemiBold, [ToolTip.TipProperty] = L.T("Open it on the canvas"),
        };
        where.Click += (_, _) =>
        {
            CanvasView.ShowScreen(screen);
            if (node.Length > 0 && exists) CanvasView.Select([node]);
        };
        var resolved = Comments.IsResolved(comment);
        var resolve = new Button { Content = resolved ? L.T("Reopen") : L.T("Resolve"), Padding = new(6, 2) };
        resolve.Click += (_, _) => Comments.SetResolved(comment, !resolved);
        var delete = new Button { Content = L.T("Delete"), Padding = new(6, 2) };
        delete.Click += (_, _) => Comments.Delete(comment);
        var reply = new TextBox { PlaceholderText = L.T("Reply… Enter"), MinWidth = 220 };
        reply.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Comments.Reply(comment, reply.Text ?? ""); };
        var body = new StackPanel { Spacing = 4, Children = { new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Children = { where, resolve, delete } } } };
        foreach (var entry in comment.Elements("Reply").Prepend(comment))
            body.Children.Add(new StackPanel
            {
                Margin = new(entry == comment ? 0 : 16, 0, 0, 0),
                Children =
                {
                    new TextBlock { Text = $"{(string?)entry.Attribute("author")} · {Date(entry)}", FontSize = 11, Opacity = 0.6 },
                    new SelectableTextBlock { Text = (string?)entry.Attribute("text") ?? "", TextWrapping = TextWrapping.Wrap },
                },
            });
        body.Children.Add(reply);
        var focused = highlight == (screen, node);
        return new Border
        {
            Padding = new(8), CornerRadius = new(6), BorderThickness = new(focused ? 2 : 1), Opacity = resolved ? 0.6 : 1,
            BorderBrush = focused ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)), Child = body,
        };
    }

    static string Date(XElement entry) =>
        DateTime.TryParse((string?)entry.Attribute("date"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
}
