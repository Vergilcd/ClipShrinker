using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;
using ClipShrinker.Core;

namespace ClipShrinker.App.Views;

/// <summary>Effect settings: speed up / slow down / pause / zoom (Timeline effects module). Hidden instead of closed, so
/// it reopens where it was. 2.9.1: holding a volume slider loops a sample at that volume (MainViewModel.StartEffectSample);
/// letting go stops it. The block heads show the effect's current key.</summary>
public partial class EffectsWindow : Window
{
    readonly MainViewModel vm;
    public bool Placed { get; set; }

    public EffectsWindow(MainViewModel vm)
    {
        this.vm = vm;
        DataContext = vm;
        InitializeComponent();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowKeys(); else vm.StopEffectSample(); };
        vm.KeysChanged += () => Dispatcher.BeginInvoke(ShowKeys);
        ShowKeys();
    }

    /// <summary>The keys on the block heads, as on the effect buttons under the preview (they can be changed).</summary>
    void ShowKeys()
    {
        foreach (var (box, act) in new[] { (SpeedKey, "speed"), (SlowKey, "slow"), (PauseKey, "pause"), (ZoomKey, "zoom") })
            box.Text = vm.Keys.TryGetValue(act, out var k) ? Text.ChordLabel(k) : "";
    }

    /// <summary>Set by the main window when the app closes; until then closing only hides.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    void Done_Click(object sender, RoutedEventArgs e) => Hide();

    /// <summary>The slider's Tag is "kind:default", e.g. "fast:70".</summary>
    static (string Kind, double Default) TagOf(object sender)
    {
        var parts = ((sender as FrameworkElement)?.Tag as string ?? "").Split(':');
        return (parts[0], parts.Length > 1 && double.TryParse(parts[1], out var d) ? d : 100);
    }

    bool listening;

    /// <summary>Pressing a volume slider (its knob or its track) starts the looping sample.</summary>
    void Vol_Down(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;                            // (the double-click resets it instead)
        listening = true;
        vm.StartEffectSample(TagOf(sender).Kind);
    }

    void Vol_Up(object sender, MouseButtonEventArgs e) => StopListening();

    /// <summary>The knob lets go of the mouse when its drag ends, also outside the window.</summary>
    void Vol_Lost(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed) StopListening();
    }

    void StopListening()
    {
        if (!listening) return;
        listening = false;
        vm.StopEffectSample();
    }

    /// <summary>Double-click a volume slider = its default.</summary>
    void Vol_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        StopListening();
        if (sender is Slider s) s.Value = TagOf(sender).Default;
    }
}
