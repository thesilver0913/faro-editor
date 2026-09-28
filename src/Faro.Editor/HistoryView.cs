using Avalonia.Controls;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>
/// The UI graph history (spec §10) as a list, like Photoshop's History panel: clicking a step undoes or redoes up to it.
/// Steps after the current one (undone) are dimmed until a new edit drops them.
/// </summary>
public sealed class HistoryView : UserControl
{
    readonly ListBox list = new();
    readonly TextBlock error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new(8, 4), IsVisible = false };
    bool building;

    public HistoryView()
    {
        list.SelectionChanged += (_, _) =>
        {
            if (building || list.SelectedIndex < 0 || list.SelectedIndex == UiHistory.Applied) return;
            var message = UiHistory.GoTo(list.SelectedIndex);
            Workspace.Reload();
            error.Text = message;
            error.IsVisible = message is not null;
        };
        DockPanel.SetDock(error, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { error, list } };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiHistory.Changed += Build;
        Build();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UiHistory.Changed -= Build;
        base.OnDetachedFromVisualTree(e);
    }

    void Build()
    {
        building = true;
        var labels = UiHistory.Labels.Prepend(L.T("Opened")).ToList();
        list.ItemsSource = labels.Select((label, i) => new ListBoxItem { Content = label, Opacity = i > UiHistory.Applied ? 0.45 : 1 }).ToList();
        list.SelectedIndex = UiHistory.Applied;
        building = false;
    }
}
