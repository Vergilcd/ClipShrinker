using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClipShrinker.App.ViewModels;
using ClipShrinker.Core;

namespace ClipShrinker.App.Views;

/// <summary>2.7 Sound track module: the Sounds lane under a clip's thumbnails. It shares the timeline's time scale and
/// zoom (TimelineEditor.X / T). Sound files dropped here (from Explorer or the Sounds list) land where they are dropped;
/// a sound is moved by dragging its body, trimmed by dragging its ends, removed with a right-click or Delete.</summary>
public sealed class SoundLaneView : FrameworkElement
{
    /// <summary>Drag-and-drop data of a sound in the Sounds list: its file path.</summary>
    public const string SoundFormat = "ClipShrinker.Sound";

    ClipItemViewModel? vm;
    enum Grab { None, Move, Left, Right }
    Grab grab;
    SoundBlock? dragBlock;
    double downX, origAt, origIn, origLen;
    bool pushed;
    double? dropX;

    static readonly Brush Bg = Frozen(Color.FromRgb(0x17, 0x17, 0x17));
    static readonly Brush BlockFill = Frozen(Color.FromRgb(0x0F, 0x6E, 0x56));
    static readonly Brush BlockSel = Frozen(Color.FromRgb(0x1D, 0x9E, 0x75));
    static readonly Brush Wave = Frozen(Color.FromArgb(0xC0, 0x9F, 0xE1, 0xCB));
    static readonly Pen Edge = new(Frozen(Color.FromRgb(0x5D, 0xCA, 0xA5)), 1);
    static readonly Pen EdgeSel = new(Brushes.White, 2);
    static readonly Brush Hint = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    static readonly Typeface Ui = new("Segoe UI");
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public SoundLaneView()
    {
        AllowDrop = true;
        Focusable = false;
        DataContextChanged += (_, _) => Attach(DataContext as ClipItemViewModel);
        SoundPeakCache.Loaded += () => Dispatcher.BeginInvoke(InvalidateVisual);
        ToolTip = "Sounds: drop sound files here or drag them from Sounds. Drag to move, drag the ends to trim, right-click removes.";
    }

    void Attach(ClipItemViewModel? v)
    {
        if (vm is not null) vm.Changed -= Refresh;
        vm = v;
        if (vm is not null) vm.Changed += Refresh;
        Refresh();
    }

    void Refresh()
    {
        Height = vm?.SoundLaneHeight ?? 0;
        Visibility = Height > 0 ? Visibility.Visible : Visibility.Collapsed;
        InvalidateVisual();
    }

    MainViewModel? Host => Window.GetWindow(this)?.DataContext as MainViewModel;

    // ------------------------------------------------------------------ drawing
    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (vm is null || W < 4 || H < 1) return;
        var ed = vm.Editor;
        bool act = ed.Active;
        var g = Rounded(W, H, act ? 7 : 4);
        dc.PushClip(g);
        dc.DrawRectangle(Bg, null, new Rect(0, 0, W, H));
        var sel = Host?.SelectedSound;
        var pcs = vm.Row.TimePieces();                   // 2.9: a sound plays real seconds, so speed sections / pauses change its width
        foreach (var b in vm.Row.Sounds)
        {
            double xa = ed.X(b.At, W), xb = ed.X(Sounds.SrcEnd(b, pcs), W);
            if (xb < -4 || xa > W + 4) continue;
            var r = new Rect(new Point(xa, act ? 3 : 1), new Point(Math.Max(xa + 2, xb), H - (act ? 4 : 1)));
            bool on = ReferenceEquals(b, sel);
            dc.DrawRoundedRectangle(on ? BlockSel : BlockFill, on ? EdgeSel : Edge, r, 3, 3);
            if (!act) continue;
            dc.PushClip(new RectangleGeometry(r));
            if (SoundPeakCache.Get(b.Path) is { Length: > 0 } pk)                      // waveform of the part that plays
            {
                double mid = (r.Top + r.Bottom) / 2, amp = (r.Bottom - r.Top - 4) / 2;   // the whole block height; the name sits on top
                double out0 = Timing.SrcToOutX(pcs, b.At);
                for (double x = Math.Max(r.Left, 0); x < Math.Min(r.Right, W); x += 2)
                {
                    double t = b.In + Timing.SrcToOutX(pcs, ed.T(x, W)) - out0;   // (the part of the file heard there)
                    int i = (int)(t * 50);
                    if (i < 0 || i >= pk.Length) continue;
                    double h = Math.Max(1, pk[i] * amp);
                    dc.DrawRectangle(Wave, null, new Rect(x, mid - h, 1.2, 2 * h));
                }
            }
            string label = b.Name + (b.VolumeDb != 0 ? $"  {Core.Modules.Equalizer.FormatDb(b.VolumeDb)}" : "")
                                  + (b.Fade >= 0.05 ? $"  fade {b.Fade.ToString("0.0", CultureInfo.InvariantCulture)} s" : "");
            DrawText(dc, label, Math.Max(r.Left, 0) + 5, r.Top + 1, 10, Brushes.White);
            dc.Pop();
        }
        if (act && vm.Row.Sounds.Count == 0)
            DrawText(dc, "Sounds: drop a sound file here, or drag one from Sounds", 8, H / 2 - 7, 10, Hint);
        if (act)
        {
            double x = ed.X(ed.Playhead, W);
            dc.DrawLine(new Pen(Frozen(Color.FromRgb(0xFB, 0xBF, 0x24)), 2), new Point(x, 0), new Point(x, H));
        }
        if (dropX is { } dx) dc.DrawLine(new Pen(Brushes.White, 2) { DashStyle = DashStyles.Dash }, new Point(dx, 0), new Point(dx, H));
        dc.Pop();
    }

    /// <summary>The lane is the bottom of the clip card: its lower corners follow the card's rounding.</summary>
    static System.Windows.Media.Geometry Rounded(double w, double h, double r)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w, 0), false, false);
            c.LineTo(new Point(w, h - r), false, false);
            c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r, h), false, false);
            c.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush) =>
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Ui, size, brush,
                                      VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));

    // ------------------------------------------------------------------ mouse
    (SoundBlock? B, Grab G) HitTest(double x)
    {
        if (vm is null) return (null, Grab.None);
        var ed = vm.Editor;
        double W = ActualWidth;
        var pcs = vm.Row.TimePieces();
        foreach (var b in vm.Row.Sounds.AsEnumerable().Reverse())        // the one drawn last is on top
        {
            double xa = ed.X(b.At, W), xb = ed.X(Sounds.SrcEnd(b, pcs), W);
            if (x < xa - 4 || x > xb + 4) continue;
            if (Math.Abs(x - xa) <= 5) return (b, Grab.Left);
            if (Math.Abs(x - xb) <= 5) return (b, Grab.Right);
            if (x >= xa && x <= xb) return (b, Grab.Move);
        }
        return (null, Grab.None);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (vm is null || Host is not { } host) return;
        host.StopPlayback();
        if (!vm.Editor.Active) { host.ClickRow(vm, false, false); e.Handled = true; return; }
        var (b, gr) = HitTest(e.GetPosition(this).X);
        host.SelectSound(b is null ? null : vm, b);
        if (b is not null)
        {
            (grab, dragBlock, downX, origAt, origIn, origLen, pushed) = (gr, b, e.GetPosition(this).X, b.At, b.In, b.Len, false);
            if (gr == Grab.Move) Cursor = AppCursors.Grabbing;           // 2.7.5: a closed hand while a sound moves
            CaptureMouse();
        }
        vm.NotifyChanged();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (vm is null) return;
        double x = e.GetPosition(this).X;
        if (dragBlock is not { } b || e.LeftButton != MouseButtonState.Pressed)
        {
            var (_, gr) = vm.Editor.Active ? HitTest(x) : (null, Grab.None);
            Cursor = gr switch { Grab.Left or Grab.Right => Cursors.SizeWE, Grab.Move => Cursors.Hand, _ => null };
            return;
        }
        var ed = vm.Editor;
        double W = ActualWidth;
        double dt = ed.T(x, W) - ed.T(downX, W);
        if (!pushed && Math.Abs(x - downX) >= 3) { Host?.PushUndo("move sound"); pushed = true; }
        if (!pushed) return;
        switch (grab)
        {
            case Grab.Move:
                b.At = Math.Max(0, Math.Min(ed.Dur - Sounds.MinLen, origAt + dt));
                break;
            case Grab.Left:                                              // the start moves, the end stays
            {                                                            // (2.9: In / Len change by real seconds)
                var pcs = vm.Row.TimePieces();
                double o0 = Timing.SrcToOutX(pcs, origAt);
                double at = Math.Max(0, origAt + dt);
                double d = Math.Max(-origIn, Math.Min(origLen - Sounds.MinLen, Timing.SrcToOutX(pcs, at) - o0));
                (b.At, b.In, b.Len) = (Math.Max(0, Timing.OutToSrcX(pcs, o0 + d)), origIn + d, origLen - d);
                break;
            }
            case Grab.Right:
            {
                var pcs = vm.Row.TimePieces();
                double end0 = Timing.OutToSrcX(pcs, Timing.SrcToOutX(pcs, origAt) + origLen);
                b.Len = Math.Max(Sounds.MinLen, Math.Min(b.FileDur - b.In, Sounds.LenTo(b.At, end0 + dt, pcs)));
                break;
            }
        }
        b.Fade = Sounds.ClampFade(b.Fade, b.Len);
        Host?.SoundMoved(vm);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (grab == Grab.Move) Cursor = Cursors.Hand;
        dragBlock = null;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        dragBlock = null;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (vm is null || !vm.Editor.Active) return;
        var (b, _) = HitTest(e.GetPosition(this).X);
        if (b is not null) Host?.RemoveSound(vm, b);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ dropping sound files
    static IEnumerable<string> DroppedSounds(IDataObject d)
    {
        if (d.GetData(SoundFormat) is string s) return [s];
        if (d.GetData(DataFormats.FileDrop) is string[] files) return files.Where(Sounds.IsSoundFile);
        return [];
    }

    protected override void OnDragEnter(DragEventArgs e) => DragHover(e);
    protected override void OnDragOver(DragEventArgs e) => DragHover(e);

    void DragHover(DragEventArgs e)
    {
        if (vm is null || !DroppedSounds(e.Data).Any()) return;
        e.Effects = DragDropEffects.Copy;
        dropX = e.GetPosition(this).X;
        InvalidateVisual();
        e.Handled = true;                                              // (the window would treat files as videos)
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        dropX = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        dropX = null;
        InvalidateVisual();
        var files = DroppedSounds(e.Data).ToList();
        if (vm is null || Host is not { } host || files.Count == 0) return;
        double t = vm.Editor.T(e.GetPosition(this).X, ActualWidth);
        foreach (var f in files)                                       // several files: one after the other
        {
            if (!host.AddSoundAt(vm, f, t)) break;
            t = vm.Row.SoundEnd(vm.Row.Sounds[^1]);
        }
        e.Handled = true;
    }
}
