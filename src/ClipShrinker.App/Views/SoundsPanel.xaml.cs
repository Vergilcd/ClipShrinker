using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>2.7: the Sounds drop-down (Sound track module): library list, adding, and the selected sound's settings.</summary>
public partial class SoundsPanel : UserControl
{
    public SoundsPanel() => InitializeComponent();

    MainViewModel? Vm => DataContext as MainViewModel;

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add sound files", Multiselect = true,
            Filter = "Sound files|" + string.Join(";", Core.Sounds.Exts.Select(x => "*" + x)) + "|All files|*.*",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) Vm?.ImportSounds(dlg.FileNames);
    }

    void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindButton(d) is null && ItemsControl.ContainerFromElement(List, d) is ListBoxItem) Vm?.AddCurrentSound();
    }

    double gripStart;

    void Grip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (gripStart <= 0) gripStart = List.ActualHeight;
        gripStart += e.VerticalChange;
        vm.SoundListHeight = (int)Math.Round(gripStart);
    }

    void Grip_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => gripStart = 0;

    void Slider_Up(object sender, MouseButtonEventArgs e) => Vm?.EndSoundEdit();

    void Volume_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s) s.Value = 0;
        Vm?.EndSoundEdit();
    }

    // dragging a library sound onto a clip's Sounds lane
    Point? down;
    LibrarySound? downItem;

    void List_MouseDown(object sender, MouseButtonEventArgs e)
    {
        down = e.GetPosition(this);
        // (not on the play / + buttons: those are clicks)
        downItem = e.OriginalSource is DependencyObject d && FindButton(d) is null && ItemsControl.ContainerFromElement(List, d) is ListBoxItem li
            ? li.DataContext as LibrarySound : null;
    }

    void List_MouseMove(object sender, MouseEventArgs e)
    {
        if (down is not { } p0 || downItem is not { } item || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - p0.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - p0.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        down = null;
        Vm?.StopPlayback();
        DragDrop.DoDragDrop(List, new DataObject(SoundLaneView.SoundFormat, item.Path), DragDropEffects.Copy);
    }

    void List_GiveFeedback(object sender, GiveFeedbackEventArgs e) => AppCursors.OnGiveFeedback(sender, e);

    static Button? FindButton(DependencyObject d)
    {
        for (var p = d; p is not null and not ListBoxItem; p = p is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is Button b) return b;
        return null;
    }
}
