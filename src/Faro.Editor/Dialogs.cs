using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Faro.Editor;

/// <summary>Small modal dialogs (confirm / text input / message) built from plain controls.</summary>
public static class Dialogs
{
    public static async Task<bool> Confirm(Window owner, string title, string message, string ok = "OK") =>
        await Show(owner, title, message, null, ok) is not null;

    public static Task<string?> Prompt(Window owner, string title, string message) => Show(owner, title, message, new TextBox(), "OK");

    public static Task Info(Window owner, string title, Control content) => Show(owner, title, null, content, null);

    /// <summary>Returns the text box's text (or "" without one) on OK, null on Cancel/close.</summary>
    static async Task<string?> Show(Window owner, string title, string? message, Control? content, string? ok)
    {
        string? result = null;
        var window = new Window { Title = title, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 360, MaxWidth = 640, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (ok is not null)
        {
            var okButton = new Button { Content = ok, IsDefault = true };
            okButton.Click += (_, _) => { result = (content as TextBox)?.Text ?? ""; window.Close(); };
            buttons.Children.Add(okButton);
        }
        var close = new Button { Content = ok is null ? "Close" : "Cancel", IsCancel = true };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(close);
        var body = new StackPanel { Spacing = 12, Margin = new(20) };
        if (message is not null) body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (content is not null) body.Children.Add(content);
        body.Children.Add(buttons);
        window.Content = body;
        window.Opened += (_, _) => content?.Focus();
        await window.ShowDialog(owner);
        return result;
    }
}
