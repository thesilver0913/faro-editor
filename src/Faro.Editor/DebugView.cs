using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Faro.Editor;

/// <summary>Console › Debug: start/continue/step/stop, the call stack (click a frame: its line and variables) and the variables.</summary>
public sealed class DebugView : UserControl
{
    readonly TextBlock state = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    readonly ListBox frames = new();
    readonly ListBox variables = new();
    readonly Button start, resume, over, into, stop;
    bool building;

    public DebugView()
    {
        start = Icons.Button(FASymbol.Play, "Start Debugging (F6)", Debugger.Start, L.T("Debug"));
        resume = Icons.Button(FASymbol.Forward, "Continue (F8)", Debugger.Continue);
        over = Icons.Button(FASymbol.Redo, "Step Over (F10)", Debugger.StepOver);
        into = Icons.Button(FASymbol.Download, "Step Into (Shift+F10)", Debugger.StepInto);
        stop = Icons.Button(FASymbol.Stop, "Stop Debugging (Shift+F6)", Debugger.Stop);
        frames.SelectionChanged += async (_, _) => { if (!building && frames.SelectedIndex >= 0) await Debugger.Select(frames.SelectedIndex); };
        var bar = Icons.Toolbar(new WrapPanel { ItemSpacing = 4, LineSpacing = 4, Margin = new(4), Children = { start, resume, over, into, stop, state } });
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        var lists = new Grid { ColumnDefinitions = new("*, *"), ColumnSpacing = 8 };
        lists.Children.Add(Titled(L.T("Call stack"), frames, 0));
        lists.Children.Add(Titled(L.T("Variables"), variables, 1));
        Content = new DockPanel { Children = { bar, lists } };
    }

    static Control Titled(string title, Control list, int column)
    {
        var header = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Margin = new(6, 2) };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        return new DockPanel { Children = { header, list }, [Grid.ColumnProperty] = column };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Debugger.Changed += Show;
        Workspace.Changed += Show;
        Show();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Debugger.Changed -= Show;
        Workspace.Changed -= Show;
        base.OnDetachedFromVisualTree(e);
    }

    void Show()
    {
        var stopped = Debugger.Stopped is not null;
        start.IsEnabled = !Debugger.Active && Workspace.Trusted;
        (resume.IsEnabled, over.IsEnabled, into.IsEnabled, stop.IsEnabled) = (stopped, stopped, stopped, Debugger.Active);
        state.Text = !Workspace.Trusted ? L.T("Restricted Mode: File › Trust Project… to debug.")
            : Debugger.Stopped is { } at ? L.F("Paused at {0}:{1}", Path.GetFileName(at.Path), at.Line)
            : Debugger.Active ? L.T("Running…")
            : L.T("Click left of a line number in the code to set a breakpoint, then Debug.");
        building = true;
        frames.ItemsSource = Debugger.Frames.Select(f => f.Path is null ? f.Name : $"{f.Name}  ({Path.GetFileName(f.Path)}:{f.Line})").ToList();
        frames.SelectedIndex = Debugger.Frames.FindIndex(f => f.Path == Debugger.Stopped?.Path && f.Line == Debugger.Stopped?.Line);
        building = false;
        variables.ItemsSource = Debugger.Variables.Select(v => $"{v.Name} = {v.Value}{(v.Type is { } t ? $"  ({t})" : "")}").ToList();
    }
}
