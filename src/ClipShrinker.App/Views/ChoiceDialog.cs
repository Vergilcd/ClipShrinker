using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClipShrinker.App.Views;

/// <summary>Small modal question with your own button labels (reference ChoiceDialog). Result = index of the
/// pressed button; Escape or closing = the cancel index.</summary>
public sealed class ChoiceDialog : Window
{
    public int Result { get; private set; }

    public ChoiceDialog(string title, string text, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex)
    {
        Title = title;
        Result = cancelIndex;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        var box = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        box.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 0, 0, 18) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Count; i++)
        {
            int k = i;
            var b = new Button { Content = buttons[i], Margin = new Thickness(i > 0 ? 8 : 0, 0, 0, 0), Padding = new Thickness(14, 5, 14, 6) };
            if (i == defaultIndex) { b.SetResourceReference(StyleProperty, "AccentButtonStyle"); b.IsDefault = true; }
            b.Click += (_, _) => { Result = k; Close(); };
            row.Children.Add(b);
            if (i == defaultIndex) Loaded += (_, _) => b.Focus();
        }
        box.Children.Add(row);
        Content = box;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Result = cancelIndex; Close(); }
        };
    }
}
