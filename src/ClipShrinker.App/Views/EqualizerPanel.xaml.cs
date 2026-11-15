using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>2.6: the Sound equalizer plugin's card over the preview (main window and full screen). The value boxes
/// take typed dB values: Enter confirms, Esc puts the shown value back.</summary>
public partial class EqualizerPanel : UserControl
{
    public EqualizerPanel()
    {
        InitializeComponent();
        Margin = new Thickness(0, 0, 0, 46);                   // above the bottom row of overlay buttons
        SetBinding(VisibilityProperty, new Binding(nameof(MainViewModel.ShowEqPanel)) { Converter = new BooleanToVisibilityConverter() });
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnBoxKey), true);
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is TextBox t) t.Dispatcher.BeginInvoke(t.SelectAll);
        }));
    }

    void OnBoxKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not TextBox box) return;
        var b = box.GetBindingExpression(TextBox.TextProperty);
        if (e.Key is Key.Enter or Key.Return)
        {
            b?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            b?.UpdateTarget();
            Keyboard.ClearFocus();
            Window.GetWindow(this)?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Double-click a slider = unchanged (0).</summary>
    void Slider_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s) s.Value = 0;
    }

    /// <summary>The round "sound" button of the overlay pill that opens / closes the card (lit while it is open,
    /// its icon yellow while something is adjusted). Shown only with the plugin on.</summary>
    public static Button ToggleButton()
    {
        var b = new Button
        {
            Style = (Style)Application.Current.FindResource("OverlayButton"), Content = "",
            ToolTip = "Equalizer and volume (Sound equalizer plugin)",
        };
        System.Windows.Automation.AutomationProperties.SetName(b, "Equalizer");
        b.SetBinding(ButtonBase.CommandProperty, new Binding(nameof(MainViewModel.ToggleEqPanelCommand)));
        b.SetBinding(VisibilityProperty, new Binding(nameof(MainViewModel.EqPluginOn)) { Converter = new BooleanToVisibilityConverter() });
        b.SetBinding(BackgroundProperty, new Binding(nameof(MainViewModel.EqPanelOpen))
        {
            Converter = new BoolToBrush(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Brushes.Transparent),
        });
        b.SetBinding(ForegroundProperty, new Binding(nameof(MainViewModel.EqChanged))
        {
            Converter = new BoolToBrush(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D)), Brushes.White),
        });
        return b;
    }

    sealed class BoolToBrush(Brush on, Brush off) : IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => value is true ? on : off;
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => Binding.DoNothing;
    }
}
