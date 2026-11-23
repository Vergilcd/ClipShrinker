using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipShrinker.App.ViewModels;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;
using Microsoft.Win32;

namespace ClipShrinker.App.Views;

public partial class MainWindow : Window, IDialogs
{
    readonly MainViewModel vm;
    EffectsWindow? effects;
    AboutWindow? about;
    ShortcutsWindow? shortcuts;
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public MainWindow(MainViewModel vm)
    {
        this.vm = vm;
        vm.Dialogs = this;
        DataContext = vm;
        InitializeComponent();
        KeepSelectionShown(ResCombo);
        KeepSelectionShown(CodecCombo);
        RestoreGeometry();
        RefreshHints();
        toastTimer.Tick += (_, _) => FadeToast();
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewKeyDown += OnKey;
        Closing += OnClosing;
        vm.RevealRequested += Reveal;
        vm.BracketChanged += DrawBracket;
        vm.KeysChanged += RefreshHints;
        vm.ModulesChanged += OnModulesChanged;
        PreviewImg.SizeChanged += (_, _) => DrawBracket();
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); vm.PreviewResized(); };
        BuildBracket();
        BuildTokenButtons();
        RightPill.Children.Insert(0, PicturePanel.ToggleButton());
        RightPill.Children.Insert(0, EqualizerPanel.ToggleButton());   // (2.6 Sound equalizer module)
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Progress)) AnimateProgress(vm.Progress); };
    }

    /// <summary>2.9.1: a drop-down whose list is swapped (Resolution: Size limit / Fixed bitrate lists; Codec) sends null
    /// back when its value is not in the new list. The view model ignores null, but the binding then reads the value
    /// back into the box in the middle of the swap, and the closed box could keep showing nothing (about every second
    /// project load, and when the size switch flips). Its SelectedItem binding is UpdateSourceTrigger=Explicit in the
    /// XAML, and only a real selection is sent here; the view model then sets the value after the list is in.</summary>
    static void KeepSelectionShown(ComboBox cb)
    {
        var dp = System.Windows.Controls.Primitives.Selector.SelectedItemProperty;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is not null) cb.GetBindingExpression(dp)?.UpdateSource();
        };
        // and after a list swap the value is read in again once the swap is done (a refresh of every binding at once,
        // as after AI editing, may set the value before the list)
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ComboBox)).AddValueChanged(cb, (_, _) =>
            cb.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => cb.GetBindingExpression(dp)?.UpdateTarget()));
    }

    /// <summary>The export bar glides to each new value (ffmpeg reports in steps); a reset to 0 jumps.</summary>
    void AnimateProgress(double to)
    {
        if (to <= 0 || to < ExportBar.Value - 0.5)
        {
            ExportBar.BeginAnimation(RangeBase.ValueProperty, null);
            ExportBar.Value = to;
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(450)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        ExportBar.BeginAnimation(RangeBase.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    // ------------------------------------------------------------------ preview box and zoom bracket
    readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>The frame is made at the box's size in real pixels (sharp at any display scaling).</summary>
    void PreviewBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (full is not null) return;                          // the full-screen preview sets the size meanwhile
        var dpi = VisualTreeHelper.GetDpi(this);
        vm.PreviewBox = (PreviewBox.ActualWidth * dpi.DpiScaleX, PreviewBox.ActualHeight * dpi.DpiScaleY);
        previewTimer.Stop();
        previewTimer.Start();
    }

    readonly Rectangle[] brLines = new Rectangle[4];
    readonly Dictionary<string, Rectangle> brCorners = [];
    (string Mode, ZoomSection Z, Point P0, double X, double Y, double W)? brDrag;

    // the inside of the zoom area: drags it too, so it shows the move cursor
    readonly Rectangle brInside = new() { Fill = Brushes.Transparent, Cursor = Cursors.SizeAll, Visibility = Visibility.Collapsed };

    void BuildBracket()
    {
        brInside.MouseLeftButtonDown += (_, e) => BracketDown(e, "move");
        Bracket.Children.Add(brInside);
        for (int i = 0; i < 4; i++)
        {
            var r = brLines[i] = new Rectangle { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, Cursor = Cursors.SizeAll, Visibility = Visibility.Collapsed };
            r.MouseLeftButtonDown += (_, e) => BracketDown(e, "move");
            Bracket.Children.Add(r);
        }
        foreach (var k in new[] { "nw", "ne", "sw", "se" })
        {
            var r = new Rectangle
            {
                Width = 13, Height = 13, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, Visibility = Visibility.Collapsed,
                Cursor = k is "nw" or "se" ? Cursors.SizeNWSE : Cursors.SizeNESW,
            };
            r.MouseLeftButtonDown += (_, e) => BracketDown(e, k);
            brCorners[k] = r;
            Bracket.Children.Add(r);
        }
        Bracket.MouseMove += BracketMove;
        Bracket.MouseLeftButtonUp += (_, _) => { brDrag = null; Bracket.ReleaseMouseCapture(); };
    }

    /// <summary>The white bracket shows the zoom area of the zoom section under the playhead (reference bracket_refresh).</summary>
    void DrawBracket()
    {
        var z = vm.BracketTarget;
        double pw = PreviewImg.ActualWidth, ph = PreviewImg.ActualHeight;
        bool show = z is not null && pw >= 20 && ph >= 20;
        foreach (var r in brLines.Concat(brCorners.Values).Append(brInside)) r.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        double bx = z!.X * pw, by = z.Y * ph, bw = z.W * pw, bh = z.W * ph, t = 4;
        void Place(Rectangle r, double x, double y, double w, double h) { Canvas.SetLeft(r, x); Canvas.SetTop(r, y); r.Width = Math.Max(1, w); r.Height = Math.Max(1, h); }
        Place(brInside, bx, by, bw, bh);
        Place(brLines[0], bx, by, bw, t);
        Place(brLines[1], bx, by + bh - t, bw, t);
        Place(brLines[2], bx, by, t, bh);
        Place(brLines[3], bx + bw - t, by, t, bh);
        foreach (var (k, (cx, cy)) in new Dictionary<string, (double, double)> { ["nw"] = (bx, by), ["ne"] = (bx + bw, by), ["sw"] = (bx, by + bh), ["se"] = (bx + bw, by + bh) })
        {
            Canvas.SetLeft(brCorners[k], cx - 6);
            Canvas.SetTop(brCorners[k], cy - 6);
        }
    }

    void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (vm.BracketTarget is not { } z) return;
        var p = e.GetPosition(PreviewImg);
        double pw = PreviewImg.ActualWidth, ph = PreviewImg.ActualHeight;
        if (z.X * pw <= p.X && p.X <= (z.X + z.W) * pw && z.Y * ph <= p.Y && p.Y <= (z.Y + z.W) * ph) BracketDown(e, "move");
    }

    void BracketDown(MouseButtonEventArgs e, string mode)
    {
        if (vm.BracketTarget is not { } z) return;
        vm.ActiveClip?.Editor.PushUndo("zoom area");
        brDrag = (mode, z, e.GetPosition(PreviewImg), z.X, z.Y, z.W);
        Bracket.CaptureMouse();
        e.Handled = true;
    }

    void BracketMove(object sender, MouseEventArgs e)
    {
        if (brDrag is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
        double pw = PreviewImg.ActualWidth, ph = PreviewImg.ActualHeight;
        var p = e.GetPosition(PreviewImg);
        if (d.Mode == "move")
            (d.Z.X, d.Z.Y) = ZoomArea.Move(d.X, d.Y, d.W, (p.X - d.P0.X) / pw, (p.Y - d.P0.Y) / ph);
        else
            (d.Z.X, d.Z.Y, d.Z.W) = ZoomArea.Resize(d.Mode, d.X, d.Y, d.W, p.X / pw, p.Y / ph);
        DrawBracket();
        vm.BracketMoved();
    }

    // ------------------------------------------------------------------ window position, size and divider
    static readonly Regex GeoRx = new(@"^(\d+)x(\d+)([+-]-?\d+)([+-]-?\d+)$");

    /// <summary>"WxH+X+Y" (the 1.0.8 Tk format) and the preview height ("sash"); kept on the screen.</summary>
    void RestoreGeometry()
    {
        if (vm.Geometry is { } g && GeoRx.Match(g) is { Success: true } m)
        {
            double w = Math.Max(MinWidth, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            double h = Math.Max(MinHeight, double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            double x = double.Parse(m.Groups[3].Value.TrimStart('+'), CultureInfo.InvariantCulture);
            double y = double.Parse(m.Groups[4].Value.TrimStart('+'), CultureInfo.InvariantCulture);
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            w = Math.Min(w, vw);
            h = Math.Min(h, vh);
            if (x + w < vl + 100 || x > vl + vw - 100 || y < vt || y > vt + vh - 100)
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            else
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = x;
                Top = y;
            }
            Width = w;
            Height = h;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (vm.Zoomed) WindowState = WindowState.Maximized;
        if (vm.Int("sash") is { } sash && sash >= 200) PreviewRow.Height = new GridLength(sash);
    }

    string CurrentGeometry()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        static string N(double v) => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
        static string S(double v) => (v >= 0 ? "+" : "") + N(v);
        return $"{N(r.Width)}x{N(r.Height)}{S(r.Left)}{S(r.Top)}";
    }

    void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        // keep the clip list usable: the preview may not push it below its minimum height
        double max = MainArea.ActualHeight - 160;
        if (PreviewRow.ActualHeight > max) PreviewRow.Height = new GridLength(Math.Max(200, max));
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!vm.CanClose()) { e.Cancel = true; return; }
        if (effects is not null) { effects.AllowClose = true; effects.Close(); }
        vm.SaveSession();
        vm.ShutdownPreview();
        vm.SaveSettings(CurrentGeometry(), WindowState == WindowState.Maximized, (int)Math.Round(PreviewRow.ActualHeight));
    }

    // ------------------------------------------------------------------ keys, drop, buttons
    /// <summary>Typing a value (a text box, an editable list), not a shortcut (reference _typing).</summary>
    static bool Typing => Keyboard.FocusedElement is TextBox or ComboBox { IsEditable: true };

    void OnKey(object sender, KeyEventArgs e)
    {
        if (HandleKey(e)) e.Handled = true;
    }

    /// <summary>A key press of this window or the full-screen preview: true = it was a shortcut.</summary>
    bool HandleKey(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), alt = mods.HasFlag(ModifierKeys.Alt);
        string? chord = KeyNames.Chord(e);
        string? act = chord is null ? null : vm.Keys.FirstOrDefault(kv => kv.Value == chord).Key;
        bool handled;
        if (act is not null && (!Typing || WorksWhileTyping(act, chord!))) handled = RunShortcut(act, e);
        else if (!ctrl && !alt && !Typing && KeyNames.Name(e) is { } name) handled = vm.OnPresetKey(name);
        else handled = false;
        return handled;
    }

    /// <summary>In a text field only a few shortcuts work (open, save, the windows), and only on keys the field
    /// does not need itself: an F key, or Ctrl / Alt + a key that is not copy, paste, undo ...</summary>
    static bool WorksWhileTyping(string act, string chord)
    {
        if (!C.KeysWhileTyping.Contains(act) || C.TextFieldChords.Contains(chord)) return false;
        var parts = chord.Split('+');
        bool fKey = parts[^1].Length > 1 && parts[^1][0] == 'f' && parts[^1][1..].All(char.IsDigit);
        return fKey || parts.Contains("ctrl") || parts.Contains("alt");
    }

    /// <summary>Does what a shortcut stands for (all of them can be rebound in the Shortcuts window).</summary>
    bool RunShortcut(string act, KeyEventArgs e)
    {
        switch (act)
        {
            case "shortcuts": Shortcuts_Click(this, e); break;
            case "effects":
                if (!vm.EffectsModuleOn) return false;
                ToggleEffects();
                break;
            case "delete_section" when !vm.EffectsModuleOn && !vm.SoundModuleOn: return false;
            case "export": vm.GoOrAbortCommand.Execute(null); break;
            case "open": vm.OpenProjectDialogCommand.Execute(null); break;
            case "save": vm.SaveProject(false); break;
            case "save_as": vm.SaveProject(true); break;
            case "undo": vm.Undo(); break;
            case "redo" or "redo_alt": vm.Redo(); break;
            case "select_all": vm.SelectAll(); break;
            case "clear_all": vm.ClearAllCommand.Execute(null); break;   // 2.9.1
            case "delete": vm.OnDelete(); break;
            case "delete_section": vm.OnBackspace(); break;
            case "play":
                if (vm.Exporting) return false;
                vm.TogglePlay();
                break;
            case "mute": vm.ToggleMute(); break;
            case "fullscreen": ToggleFullScreen(); break;
            default: return vm.OnActionKey(act);
        }
        return true;
    }

    // ------------------------------------------------------------------ clip cards
    ClipItemViewModel? dragRow;
    Point dragStart;
    bool dragging, dragMod;
    System.Windows.Controls.Primitives.Popup? ghost;

    void Head_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClipItemViewModel row) return;
        if (e.OriginalSource is DependencyObject d && FindParent<ButtonBase>(d) is not null) return;   // its buttons
        var mods = Keyboard.Modifiers;
        vm.StopPlayback();
        vm.BeginReorder();
        dragMod = (mods & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;  // Shift / Ctrl: selecting, not dragging
        vm.ClickRow(row, mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Control));
        dragRow = row;
        dragStart = e.GetPosition(this);
        dragging = false;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    void Head_MouseMove(object sender, MouseEventArgs e)
    {
        if (dragRow is null || dragMod || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (!dragging)
        {
            if (Math.Abs(p.Y - dragStart.Y) < 4) return;
            dragging = true;
            ((FrameworkElement)sender).Cursor = Cursors.SizeAll;
            ShowGhost(dragRow);
        }
        if (ghost is not null) { ghost.HorizontalOffset = p.X + 18; ghost.VerticalOffset = p.Y + 14; }
        // the clip goes where the pointer is: count the other cards whose middle is above it
        double y = e.GetPosition(ClipList).Y;
        int idx = 0;
        foreach (var c in vm.Clips)
        {
            if (ReferenceEquals(c, dragRow)) continue;
            if (ClipList.ItemContainerGenerator.ContainerFromItem(c) is FrameworkElement fe)
            {
                var top = fe.TranslatePoint(new Point(0, 0), ClipList).Y;
                if (top + fe.ActualHeight / 2 < y) idx++;
            }
        }
        vm.MoveClipTo(dragRow, idx);
        var sp = e.GetPosition(ClipScroll).Y;                    // scroll at the edges of the list
        if (sp < 24) ClipScroll.LineUp();
        else if (sp > ClipScroll.ActualHeight - 24) ClipScroll.LineDown();
    }

    void Head_MouseUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();
        ((FrameworkElement)sender).Cursor = null;
        if (ghost is not null) { ghost.IsOpen = false; ghost = null; }
        if (dragging && dragRow is not null) Reveal(dragRow);
        dragRow = null;
        dragging = false;
    }

    /// <summary>A small floating card (thumbnail + file name) that follows the pointer while a clip is dragged.</summary>
    void ShowGhost(ClipItemViewModel row)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        if (row.FirstThumb is { } img) panel.Children.Add(new Image { Source = img, Height = 40, Margin = new Thickness(0, 0, 10, 0) });
        panel.Children.Add(new TextBlock { Text = $"{row.Index + 1}.  {row.Name}", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var border = new Border { Child = panel, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Opacity = 0.92 };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        ghost = new System.Windows.Controls.Primitives.Popup
        {
            Child = border, AllowsTransparency = true, PlacementTarget = this, IsHitTestVisible = false,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Relative, IsOpen = true,
        };
    }

    /// <summary>Keeps the (just enlarged) active clip inside the visible part of the list.</summary>
    void Reveal(ClipItemViewModel row) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ClipList.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement fe) fe.BringIntoView();
        });

    void Fit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipItemViewModel row) { row.Editor.Fit(); row.NotifyChanged(); }
    }

    /// <summary>The wheel over a clip's preset list scrolls the clip list, it does not change the preset.</summary>
    void Combo_NoWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false })
        {
            e.Handled = true;
            ClipScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent });
        }
    }

    static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = d; p is not null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }

    /// <summary>The effect buttons under the preview, labelled with their current hotkeys (rebuilt when a key changes).</summary>
    static readonly (string Act, string Name, string Bg, string Border, string Fg, string What)[] EffectButtons =
    [
        ("speed", "Speed Up", "#3B82F6", "#3B82F6", "#FFFFFF", "a speed-up section (blue)"),
        ("slow", "Slow Down", "#EC4899", "#EC4899", "#FFFFFF", "a slow-down section (pink)"),
        ("pause", "Pause", "#F97316", "#F97316", "#FFFFFF", "a pause (orange: the picture stops, then the clip goes on)"),
        ("zoom", "Zoom", "#F4F4F5", "#C8C8C8", "#000000", "a zoom section (white)"),
    ];
    Point? chipDown;                                           // where the mouse went down on an effect button
    bool chipDragged;                                          // that press became a drag: no click

    void RefreshHints()
    {
        EffectChips.Children.Clear();
        foreach (var (act, name, bg, border, fg, what) in EffectButtons)
        {
            static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
            string key = Text.ChordLabel(vm.Keys[act]);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = "\u283F", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 13, Opacity = 0.75,
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            // 2.6: "Speed Up | S" - the name, a faint divider, then the hotkey
            content.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = "|", Opacity = 0.5, Margin = new Thickness(7, 0, 7, 1), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = key, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button
            {
                Style = (Style)FindResource("EffectChip"), Content = content, Background = B(bg), BorderBrush = B(border), Foreground = B(fg),
                ToolTip = $"Click to add {what} at the playhead of the active clip ({key}), or drag it onto a clip's timeline to add it there.",
            };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.GiveFeedback += AppCursors.OnGiveFeedback;                 // 2.7.5: a closed hand while dragged
            b.PreviewMouseLeftButtonDown += (_, e) => { chipDown = e.GetPosition(this); chipDragged = false; };
            b.PreviewMouseMove += (_, e) =>
            {
                if (chipDown is not { } p0 || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - p0.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(p.Y - p0.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                chipDown = null;
                chipDragged = true;
                vm.StopPlayback();
                DragDrop.DoDragDrop(b, new DataObject(TimelineView.EffectFormat, act), DragDropEffects.Copy);
            };
            b.Click += (_, _) =>
            {
                chipDown = null;
                if (chipDragged) { chipDragged = false; return; }
                vm.OnActionKey(act);
            };
            EffectChips.Children.Add(b);
        }
        FullscreenButton.ToolTip = $"Full-screen preview ({Text.ChordLabel(vm.Keys["fullscreen"])}). Esc leaves it.";
    }

    /// <summary>The Sounds drop-down opens: read the sound folder again (files may have been added in Explorer).</summary>
    void SoundsButton_Checked(object sender, RoutedEventArgs e) => vm.RefreshSoundLibrary();

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await vm.AddFiles(files);
    }

    void DropZone_Click(object sender, MouseButtonEventArgs e) => vm.AddVideosCommand.Execute(null);

    void Zone_MouseEnter(object sender, MouseEventArgs e) => SetZoneHover(sender, true);
    void Zone_MouseLeave(object sender, MouseEventArgs e) => SetZoneHover(sender, false);

    void SetZoneHover(object sender, bool on)
    {
        if (sender == DropZone) { DropZoneTint.Opacity = on ? 0.12 : 0.06; return; }   // 2.9.1: always accent, a bit stronger on hover
        AddStripDash.SetResourceReference(Shape.StrokeProperty, on ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush");
    }

    void ProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = ProjectButton.ContextMenu!;
        menu.PlacementTarget = ProjectButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = vm;
        menu.IsOpen = true;
    }

    void Effects_Click(object sender, RoutedEventArgs e) => ToggleEffects();

    // ------------------------------------------------------------------ full-screen preview
    FullScreenWindow? full;

    void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    void ToggleFullScreen()
    {
        if (full is not null) { full.Close(); return; }
        if (!vm.HasClips) return;
        full = new FullScreenWindow(vm, this, HandleKey);
        full.SizeChanged += (_, _) =>
        {
            if (full is null || full.WindowState != WindowState.Maximized) return;
            var dpi = VisualTreeHelper.GetDpi(full);
            vm.FullScreen = true;
            vm.PreviewBox = (full.ActualWidth * dpi.DpiScaleX, full.ActualHeight * dpi.DpiScaleY);
            vm.PreviewResized();
        };
        full.Closed += (_, _) =>
        {
            full = null;
            vm.FullScreen = false;
            PreviewBox_SizeChanged(this, null!);             // the frame at the normal size again
            Activate();
        };
        full.Show();
    }

    /// <summary>The Effects window is kept and only hidden, so it opens where it was (reference show_settings / hide_settings).
    /// It belongs to the Timeline effects module.</summary>
    void ToggleEffects()
    {
        if (!vm.EffectsModuleOn) { effects?.Hide(); return; }
        effects ??= new EffectsWindow(vm) { Owner = this };
        if (effects.IsVisible) { effects.Hide(); return; }
        if (!effects.Placed)
        {
            effects.WindowStartupLocation = WindowStartupLocation.Manual;
            effects.Left = Left + Math.Max(20, (ActualWidth - 350 - 520) / 2);
            effects.Top = Top + 50;
            effects.Placed = true;
        }
        effects.Show();
        effects.Activate();
    }

    ModulesWindow? modulesWin;

    void Modules_Click(object sender, RoutedEventArgs e)
    {
        if (modulesWin is { IsLoaded: true }) { modulesWin.Activate(); return; }
        modulesWin = new ModulesWindow(vm) { Owner = this };
        modulesWin.Show();
    }

    /// <summary>A module was switched off or deleted: its windows close.</summary>
    void OnModulesChanged()
    {
        if (!vm.EffectsModuleOn) effects?.Hide();
        if (!vm.PresetsModuleOn && presetsWin is { IsLoaded: true }) presetsWin.Close();
        if (shortcuts is { IsLoaded: true } open)                // rebuilt with / without the module sections
        {
            var (l, t) = (open.Left, open.Top);
            open.Close();
            shortcuts = new ShortcutsWindow(vm) { Owner = this, Left = l, Top = t };
            shortcuts.Show();
            modulesWin?.Activate();
        }
    }

    void About_Click(object sender, RoutedEventArgs e)
    {
        if (about is { IsLoaded: true }) { about.Activate(); return; }
        about = new AboutWindow { Owner = this };
        about.Show();
    }

    void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (shortcuts is { IsLoaded: true }) { shortcuts.Activate(); return; }
        shortcuts = new ShortcutsWindow(vm) { Owner = this, Left = Left + 120, Top = Top + 90 };
        shortcuts.Show();
    }

    PresetsWindow? presetsWin;

    void Presets_Click(object sender, RoutedEventArgs e)
    {
        if (presetsWin is { IsLoaded: true }) { presetsWin.Activate(); return; }
        presetsWin = new PresetsWindow(vm) { Owner = this, WindowStartupLocation = WindowStartupLocation.Manual };
        presetsWin.Left = Left + Math.Max(20, ActualWidth - 640);
        presetsWin.Top = Top + 50;
        presetsWin.Show();
    }

    // ------------------------------------------------------------------ file name fields
    TextBox? lastNameBox;                                      // Prefix or Suffix, whichever had the cursor last

    void NameBox_Focus(object sender, KeyboardFocusChangedEventArgs e) => lastNameBox = sender as TextBox;

    void BuildTokenButtons()
    {
        foreach (var (token, label, help) in NameTokens.All)
        {
            var b = new Button { Content = label, Style = (Style)FindResource("TokenButton"), ToolTip = $"{token}  -  {help}" };
            b.Click += (_, _) => InsertToken(token);
            TokenPanel.Children.Add(b);
        }
    }

    /// <summary>Puts the field where the cursor is (replacing a selection) in the last used box (Prefix at first).</summary>
    void InsertToken(string token)
    {
        var box = lastNameBox ?? PrefixBox;
        int at = box.SelectionStart, len = box.SelectionLength;
        box.Text = box.Text.Remove(at, len).Insert(at, token);
        box.Focus();
        box.Select(at + token.Length, 0);
    }

    void PickSaveDir_Click(object sender, RoutedEventArgs e)
    {
        string start = vm.SaveDir.Trim() is { Length: > 0 } d && Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (PickFolder("Default save folder", start) is { } f) vm.SaveDir = f;
    }

    // ------------------------------------------------------------------ IDialogs
    public void Info(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void Warn(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    public void Error(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Error);
    public bool YesNo(string title, string text) =>
        MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public int Choice(string title, string text, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex)
    {
        var d = new ChoiceDialog(title, text, buttons, defaultIndex, cancelIndex) { Owner = this };
        d.ShowDialog();
        return d.Result;
    }

    public string[] OpenVideos()
    {
        var d = new OpenFileDialog
        {
            Title = "Add videos", Multiselect = true,
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.m4v;*.avi;*.webm|All files|*.*",
        };
        return d.ShowDialog(this) == true ? d.FileNames : [];
    }

    public string? OpenProject()
    {
        var d = new OpenFileDialog { Title = "Open project", Filter = $"Clip Shrinker project|*{C.ProjectExt}|All files|*.*" };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public string? SaveFile(string title, string fileName, string? folder)
    {
        var d = new SaveFileDialog { Title = title, FileName = fileName, DefaultExt = ".mp4", Filter = "MP4 video|*.mp4", AddExtension = true };
        if (folder is not null && Directory.Exists(folder)) d.InitialDirectory = folder;
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public string? SaveProjectFile(string fileName, string? folder)
    {
        var d = new SaveFileDialog
        {
            Title = "Save project", FileName = fileName, DefaultExt = C.ProjectExt, AddExtension = true,
            Filter = $"Clip Shrinker project|*{C.ProjectExt}|All files|*.*",
        };
        if (folder is not null && Directory.Exists(folder)) d.InitialDirectory = folder;
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public string? PickFolder(string title, string? folder)
    {
        var d = new OpenFolderDialog { Title = title };
        if (folder is not null && Directory.Exists(folder)) d.InitialDirectory = folder;
        return d.ShowDialog(this) == true ? d.FolderName : null;
    }

    /// <summary>Short message over the bottom of the preview: shown for 3 s, then it fades out.</summary>
    public void Toast(string text)
    {
        ToastText.Text = text;
        ToastBox.BeginAnimation(OpacityProperty, null);
        ToastBox.Opacity = 1;
        ToastBox.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
    }

    void FadeToast()
    {
        toastTimer.Stop();
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(360));
        a.Completed += (_, _) => { if (ToastBox.Opacity == 0) ToastBox.Visibility = Visibility.Collapsed; };
        ToastBox.BeginAnimation(OpacityProperty, a);
    }
}
