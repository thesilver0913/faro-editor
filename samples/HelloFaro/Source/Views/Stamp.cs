using Avalonia.Controls;
using Avalonia.Media;
using Faro.Runtime;

namespace MyApp.Views;

/// <summary>A Script node's class: its look, button and behaviour are all written in code.</summary>
public class Stamp : FaroScript
{
    int stamps;

    public override Control Build()
    {
        var label = new TextBlock { Text = "スタンプ: 0" };
        var button = new Button { Content = "押す" };
        button.Click += (_, _) => label.Text = $"スタンプ: {++stamps}";
        return new Border
        {
            Background = Brushes.AliceBlue,
            CornerRadius = new(8),
            Padding = new(12),
            Child = new StackPanel { Spacing = 8, Children = { label, button } },
        };
    }
}
