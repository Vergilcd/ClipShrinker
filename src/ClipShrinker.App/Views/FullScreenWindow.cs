using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>The preview on the whole screen (2.1): the same picture as the main window's preview, with play, sound,
/// volume and "leave full screen" over it. The controls and the pointer hide after a moment without mouse movement
/// while playing. Esc or the full-screen shortcut closes it; the other shortcuts keep working.</summary>
public sealed class FullScreenWindow : Window
{
    readonly MainViewModel vm;
    readonly Grid overlay;
    readonly DispatcherTimer idle = new() { Interval = TimeSpan.FromSeconds(2.5) };

    public FullScreenWindow(MainViewModel vm, Window owner, Func<KeyEventArgs, bool> keys)
    {
        this.vm = vm;
        DataContext = vm;
        Owner = owner;
        Title = "Preview";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        // on the screen the main window is on
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + 40;
        Top = owner.Top + 40;
        Width = 400;
        Height = 300;
        Loaded += (_, _) => WindowState = WindowState.Maximized;

        var img = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        img.SetBinding(Image.SourceProperty, new Binding(nameof(MainViewModel.PreviewImage)));

        var play = OverlayButton(nameof(MainViewModel.PlayIcon), "Play / pause (Space)");
        play.Command = vm.TogglePlayCommand;
        var mute = OverlayButton(nameof(MainViewModel.SpeakerIcon), "Preview sound on / off (M)");
        mute.Command = vm.ToggleMuteCommand;
        var vol = new Slider
        {
            Width = 110, Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 14, 0),
            Focusable = false, IsMoveToPointEnabled = true, ToolTip = "Preview volume",
        };
        vol.SetBinding(RangeBase.ValueProperty, new Binding(nameof(MainViewModel.PvVol)));
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(play);
        left.Children.Add(mute);
        left.Children.Add(vol);
        var exit = new Button { Style = (Style)Application.Current.FindResource("OverlayButton"), Content = "", ToolTip = "Leave full screen (Esc)" };
        exit.Click += (_, _) => Close();

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(PicturePanel.ToggleButton());        // (Picture adjustments plugin)
        right.Children.Add(exit);

        overlay = new Grid { Margin = new Thickness(16) };
        overlay.Children.Add(Pill(left, HorizontalAlignment.Left));
        overlay.Children.Add(Pill(right, HorizontalAlignment.Right));
        overlay.Children.Add(new PicturePanel());

        var root = new Grid();
        root.Children.Add(img);
        root.Children.Add(overlay);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (keys(e)) e.Handled = true;
        };
        MouseMove += (_, _) => ShowControls();
        MouseLeftButtonDown += (_, e) =>               // a double-click on the picture leaves full screen too
        {
            if (e.ClickCount == 2 && e.OriginalSource is Image) Close();
        };
        idle.Tick += (_, _) =>
        {
            idle.Stop();
            if (vm.Playing && !overlay.IsMouseOver) { overlay.Opacity = 0; Cursor = Cursors.None; }
        };
        vm.PropertyChanged += OnVm;
        Closed += (_, _) => { vm.PropertyChanged -= OnVm; idle.Stop(); };
        ShowControls();
    }

    void OnVm(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Playing)) ShowControls();
    }

    void ShowControls()
    {
        overlay.Opacity = 1;
        Cursor = null;
        idle.Stop();
        if (vm.Playing) idle.Start();
    }

    static Button OverlayButton(string iconProperty, string tip)
    {
        var b = new Button { Style = (Style)Application.Current.FindResource("OverlayButton"), ToolTip = tip };
        b.SetBinding(ContentProperty, new Binding(iconProperty));
        return b;
    }

    static Border Pill(UIElement child, HorizontalAlignment h) => new()
    {
        Child = child, HorizontalAlignment = h, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(19),
        Padding = new Thickness(2), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
    };
}
