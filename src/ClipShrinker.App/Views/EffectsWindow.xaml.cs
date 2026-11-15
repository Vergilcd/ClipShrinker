using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>Speed up / slow motion / zoom effects (Timeline effects plugin). Hidden instead of closed, so it reopens
/// where it was. (The effect sample play buttons and the RIFE download come with the preview audio.)</summary>
public partial class EffectsWindow : Window
{
    public bool Placed { get; set; }

    public EffectsWindow(MainViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
    }

    /// <summary>Set by the main window when the app closes; until then closing only hides.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    void Done_Click(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Double-click a volume slider = its default (the Tag).</summary>
    void Vol_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s && double.TryParse(s.Tag?.ToString(), out var v)) s.Value = v;
    }
}
