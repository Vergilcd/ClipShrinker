using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>Transitions, speed / slow motion / zoom effects and app options. Hidden instead of closed, so it reopens
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

    // ------------------------------------------------------------------ transition hover preview
    // A small looping preview next to the open list while the mouse is over an entry (reference _trans_hover_setup).
    System.Windows.Controls.Primitives.Popup? hoverPopup;
    Image? hoverImage;
    TextBlock? hoverCaption;
    string? hoverName;
    readonly System.Windows.Threading.DispatcherTimer hoverTimer = new();
    List<System.Windows.Media.Imaging.BitmapSource> hoverFrames = [];
    int hoverIndex;

    /// <summary>The open list holds the mouse capture, so its items get no MouseEnter: find the item under the pointer.</summary>
    void Trans_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!TransCombo.IsDropDownOpen) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ComboBoxItem item) { HoverItem(item); return; }
    }

    void HoverItem(ComboBoxItem item)
    {
        if (item.Content is not string name || name == hoverName) return;
        if (DataContext is not MainViewModel vm) return;
        hoverName = name;
        hoverTimer.Stop();
        hoverFrames = [];
        EnsurePopup();
        hoverImage!.Source = null;
        hoverCaption!.Text = $"{name}  (rendering...)";
        hoverPopup!.PlacementTarget = item;
        hoverPopup.IsOpen = false;
        hoverPopup.IsOpen = true;
        var src = vm.CurrentTransitionSources();
        Task.Run(() => vm.TransitionFrames(name, src)).ContinueWith(t =>
        {
            if (hoverName != name || hoverPopup is null) return;
            hoverFrames = t.Result;
            hoverIndex = 0;
            hoverCaption.Text = hoverFrames.Count > 0 ? name : $"{name}  (no preview)";
            Tick();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void EnsurePopup()
    {
        if (hoverPopup is not null) return;
        hoverImage = new Image { Width = Core.C.PvW, Height = Core.C.PvH, Stretch = System.Windows.Media.Stretch.Fill };
        hoverCaption = new TextBlock { Foreground = System.Windows.Media.Brushes.Gainsboro, FontSize = 11, Margin = new Thickness(4, 2, 4, 2) };
        var panel = new StackPanel { Background = System.Windows.Media.Brushes.Black };
        panel.Children.Add(hoverImage);
        panel.Children.Add(new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C)), Child = hoverCaption });
        var border = new Border { Padding = new Thickness(2), Child = panel };
        border.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        hoverPopup = new System.Windows.Controls.Primitives.Popup
        {
            Child = border, AllowsTransparency = true, IsHitTestVisible = false, HorizontalOffset = 6,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Right,
        };
        hoverTimer.Tick += (_, _) => Tick();
    }

    void Tick()
    {
        hoverTimer.Stop();
        if (hoverFrames.Count == 0 || hoverImage is null || hoverPopup is not { IsOpen: true }) return;
        hoverImage.Source = hoverFrames[hoverIndex % hoverFrames.Count];
        hoverIndex++;
        hoverTimer.Interval = TimeSpan.FromMilliseconds(hoverIndex % hoverFrames.Count == 0 ? 500 : 1000 / Core.C.PvFps);   // pause before it loops
        hoverTimer.Start();
    }

    void Trans_DropDownClosed(object? sender, EventArgs e)
    {
        hoverTimer.Stop();
        hoverName = null;
        if (hoverPopup is not null) hoverPopup.IsOpen = false;
    }

    /// <summary>Double-click a volume slider = its default (the Tag).</summary>
    void Vol_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s && double.TryParse(s.Tag?.ToString(), out var v)) s.Value = v;
    }
}
