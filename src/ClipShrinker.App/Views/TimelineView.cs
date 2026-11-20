using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipShrinker.App.ViewModels;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;
using static ClipShrinker.Core.Project.TimelineEditor;

namespace ClipShrinker.App.Views;

/// <summary>
/// One clip's timeline: thumbnails, trim handles with time boxes, speed sections (blue / pink, with multiplier boxes),
/// zoom sections (white), time marks and overview bar when zoomed in, and the yellow playhead
/// (reference ClipRow.redraw). All editing logic is in Core's TimelineEditor; this only draws and forwards input.
/// </summary>
public sealed class TimelineView : Canvas
{
    /// <summary>Drag-and-drop data of an effect button under the preview: "speed", "slow", "pause" or "zoom".</summary>
    public const string EffectFormat = "ClipShrinker.Effect";
    double? dropX;                                             // an effect button is dragged over this timeline
    ClipItemViewModel? vm;
    TimelineEditor? Ed => vm?.Editor;
    readonly TextBox startBox = TimeBox("#22C55E"), endBox = TimeBox("#EF4444");
    readonly Dictionary<SpeedSection, TextBox> speedBoxes = new(ReferenceEqualityComparer.Instance);

    static readonly Brush Bg = Frozen("#202020");
    static readonly Brush Shade = Frozen(Color.FromArgb(166, 0, 0, 0));        // trimmed away (65 %, plus the stripes)
    static readonly Pen HatchDark = FrozenPen(Color.FromArgb(38, 0, 0, 0), 5.5);         // 15 % black
    static readonly Pen HatchLight = FrozenPen(Color.FromArgb(18, 255, 255, 255), 5.5);  // 7 % white (white stands out more)
    static Pen FrozenPen(Color c, double w) { var p = new Pen(Frozen(c), w); p.Freeze(); return p; }
    const double HatchStep = 16;

    /// <summary>Slanted stripes over a trimmed-away part (2.2.2): 15 % black in dark mode, 7 % white in light mode.
    /// Drawn as real lines (a tiled brush was rendered at low resolution and scaled up, which made the edges jagged
    /// on scaled displays). They lie on a grid in timeline coordinates, so both trimmed-away ends line up.</summary>
    void DrawHatch(DrawingContext dc, Rect r)
    {
        var bg = (TryFindResource("ApplicationBackgroundBrush") as SolidColorBrush)?.Color ?? Col("#202020");
        var pen = bg.R + bg.G + bg.B > 3 * 128 ? HatchLight : HatchDark;
        dc.PushClip(new RectangleGeometry(r));
        // lines x + y = k * step, going up to the right. Each line starts and ends well outside the rectangle, so
        // the clip cuts it straight along the edges: the square ends of the pen never show as slanted corners.
        double e = 2 * pen.Thickness, top = r.Top - e, bottom = r.Bottom + e;
        double k0 = Math.Floor((r.Left + top) / HatchStep) - 1, k1 = Math.Ceiling((r.Right + bottom) / HatchStep) + 1;
        for (double k = k0; k <= k1; k++)
        {
            double s = k * HatchStep;
            dc.DrawLine(pen, new Point(s - bottom, bottom), new Point(s - top, top));
        }
        dc.Pop();
    }
    static readonly Brush Yellow = Frozen("#FBBF24");
    static readonly Brush Dark111 = Frozen("#111111");
    static readonly Typeface Ui = new("Segoe UI");
    static readonly Typeface UiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public TimelineView()
    {
        ClipToBounds = true;                                   // (plus rounded bottom corners, see UpdateClip)
        Background = Bg;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        Children.Add(startBox);
        Children.Add(endBox);
        HookTimeBox(startBox, true);
        HookTimeBox(endBox, false);
        DataContextChanged += (_, _) => Attach(DataContext as ClipItemViewModel);
        SizeChanged += (_, _) => { if (vm is not null) vm.SetWidth(ActualWidth); UpdateClip(); Redraw(); };
    }


    static SolidColorBrush Frozen(string hex) => Frozen((Color)ColorConverter.ConvertFromString(hex));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static Color Col(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Brush Alpha(string hex, double a) { var c = Col(hex); c.A = (byte)(255 * a); return Frozen(c); }

    void Attach(ClipItemViewModel? v)
    {
        if (vm is not null) vm.Changed -= Redraw;
        vm = v;
        if (vm is not null)
        {
            vm.Changed += Redraw;
            if (ActualWidth > 0) vm.SetWidth(ActualWidth);
        }
        Redraw();
    }

    MainViewModel? Host => Window.GetWindow(this)?.DataContext as MainViewModel;

    double W => ActualWidth;

    public void Redraw()
    {
        if (vm is null) return;
        Height = vm.TimelineHeight;
        UpdateClip();
        SyncBoxes();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ drawing
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (vm is null || Ed is not { } ed || W < 2 * ed.Pad + 2) return;
        var clip = ed.Clip;
        double W0 = W, tlb = ed.Height, dur = ed.Dur;
        bool act = ed.Active;
        bool ov = ed.Overlay;                                // 2.0: headers lie on the thumbnails (no lanes above them)
        double y0 = ed.StripTop, y1 = tlb;
        if (!act) (y0, y1) = (0, tlb);                      // strip: markers span the full thumbnail height
        double Lx = ed.Pad, Rx = W0 - ed.Pad;
        double X(double t) => ed.X(t, W0);
        double Cl(double x) => Math.Min(Math.Max(x, Lx), Rx);
        double tw = ed.ThumbWidth;

        foreach (var (key, img) in vm.Cache.OrderBy(k => (k.Key.N1, k.Key.L, k.Key.K)))
        {
            double x = X(ed.ThumbTime(key.L, key.K, key.N1));
            if (-tw < x && x < W0 + tw)
                dc.DrawImage(img, new Rect(x - img.Width / 2, (y0 + y1) / 2 - img.Height / 2, img.Width, img.Height));
        }
        double xs = X(clip.Start), xe = X(clip.End), x00 = X(0), x11 = X(dur);
        if (x00 > 0) dc.DrawRectangle(Bg, null, R(0, y0, x00, y1));          // nothing is shown outside the video
        if (x11 < W0) dc.DrawRectangle(Bg, null, R(x11, y0, W0, y1));
        double mid = (y0 + y1) / 2;
        double ty0 = Math.Max(y0, mid - ed.ThumbHeight / 2), ty1 = Math.Min(y1, mid + ed.ThumbHeight / 2);
        foreach (var r in new[] { xs > Math.Max(x00, 0) ? R(Math.Max(x00, 0), ty0, Math.Min(xs, W0), ty1) : Rect.Empty,
                                  xe < Math.Min(x11, W0) ? R(Math.Max(xe, 0), ty0, Math.Min(x11, W0), ty1) : Rect.Empty })
        {
            if (r.IsEmpty) continue;
            dc.DrawRectangle(Shade, null, r);
            DrawHatch(dc, r);
        }
        double lineW = ov ? 3 : (act ? 4 : 3);               // section edges: about twice the 2 px playhead

        // speed sections: tinted strip and edge lines; the header (same colour as the lines) is drawn further down
        var headers = new List<Action>();
        foreach (var (sec, a, b) in ed.Vis())
        {
            double xa = X(a), xb = X(b);
            if (xb < Lx - 6 || xa > Rx + 6) continue;
            double ca = Cl(xa), cb = Cl(xb);
            bool on = ReferenceEquals(sec, ed.Sel);
            bool slow = sec.Speed < 1;
            string[] col = sec.IsPause ? (on ? ["#FED7AA", "#FB923C", "#FFF7ED", "#FFEDD5"] : ["#F97316", "#F97316", "#EA580C", "#FDBA74"])
                         : slow ? (on ? ["#FBCFE8", "#F472B6", "#FDF2F8", "#FCE7F3"] : ["#EC4899", "#EC4899", "#DB2777", "#F9A8D4"])
                                : (on ? ["#5EEAD4", "#22D3EE", "#ECFEFF", "#CFFAFE"] : ["#3B82F6", "#3B82F6", "#2563EB", "#93C5FD"]);
            dc.DrawRectangle(Alpha(col[0], on ? 0.5 : 0.25), null, R(ca, y0, cb, y1));
            foreach (double xx in new[] { xa, xb })
                if (Lx - 6 <= xx && xx <= Rx + 6)
                    dc.DrawLine(ov ? new Pen(Frozen(col[3]), lineW) : RoundPen(col[3], lineW), new Point(xx, act ? (ov ? 0 : 8) : y0), new Point(xx, act ? ty1 : y1));
            if (act && ov) headers.Add(() => dc.DrawRectangle(Frozen(col[3]), null, R(ca, 0, cb, OvSpeedH)));
            else if (act) dc.DrawRectangle(Frozen(col[1]), new Pen(Frozen(col[2]), on ? 2 : 1), R(ca, 4, cb, LaneH - 4));
        }

        foreach (var (z, a, b) in ed.VisZ())                                  // zoom sections
        {
            double xa = X(a), xb = X(b);
            if (xb < Lx - 6 || xa > Rx + 6) continue;
            double ca = Cl(xa), cb = Cl(xb);
            bool on = ReferenceEquals(z, ed.ZSel);
            string line = on ? "#FFFFFF" : "#A1A1AA";
            dc.DrawRectangle(Alpha("#FFFFFF", on ? 0.25 : 0.12), null, R(ca, y0, cb, y1));
            if (act && ov)
            {
                var (ly0, ly1) = ed.ZoomLane;                // at the bottom, so it never covers the speed headers
                foreach (double xx in new[] { xa, xb })
                    if (Lx - 6 <= xx && xx <= Rx + 6)
                        dc.DrawLine(new Pen(Frozen(line), lineW), new Point(xx, 0), new Point(xx, ly1));
                headers.Add(() =>
                {
                    dc.DrawRectangle(Frozen(line), null, R(ca, ly0, cb, ly1));
                    if (cb - ca > 36) DrawText(dc, "ZOOM", (ca + cb) / 2, (ly0 + ly1) / 2, 8, Dark111, UiBold, center: true, vCenter: true);
                });
            }
            else if (act)
            {
                double ly0 = LaneH + 1, ly1 = ed.LaneHeight - 2;
                dc.DrawRectangle(Frozen(on ? "#FFFFFF" : "#8E8E96"), new Pen(Frozen(on ? "#111111" : "#55555C"), on ? 2 : 1), R(ca, ly0, cb, ly1));
                if (cb - ca > 40) DrawText(dc, "ZOOM", (ca + cb) / 2, (ly0 + ly1) / 2, 9, Dark111, UiBold, center: true, vCenter: true);
                foreach (double xx in new[] { xa, xb })
                    if (Lx - 6 <= xx && xx <= Rx + 6)
                        dc.DrawLine(RoundPen(line, 4), new Point(xx, ly0 - 2), new Point(xx, ty1));
            }
            else
            {
                if (ov)                                      // the thin strip shows the zoom's edge lines too
                    foreach (double xx in new[] { xa, xb })
                        if (Lx - 6 <= xx && xx <= Rx + 6)
                            dc.DrawLine(new Pen(Frozen(line), lineW), new Point(xx, y0), new Point(xx, y1));
                dc.DrawRectangle(Brushes.White, null, R(ca, y0, cb, y0 + 3));
            }
        }

        foreach (var (xx, colr, d) in new[] { (xs, "#22C55E", -1), (xe, "#EF4444", 1) })   // trim handles: bar + flags
        {
            if (!(Lx - 6 <= xx && xx <= Rx + 6)) continue;
            double bw = act ? 6 : 4, fl = act ? 7 : 5, h = bw / 2, xo = xx + d * h;
            var br = Frozen(colr);
            dc.DrawRectangle(br, null, R(xx - h, ty0, xx + h, ty1));
            dc.DrawGeometry(br, null, Poly(new(xo, ty0), new(xo + d * fl, ty0), new(xo, ty0 + fl)));
            dc.DrawGeometry(br, null, Poly(new(xo, ty1), new(xo + d * fl, ty1), new(xo, ty1 - fl)));
        }

        foreach (var h in headers) h();                                       // effect headers on top of the picture

        foreach (var (t, label) in ed.Ticks(W0))                              // time marks (under the speed headers)
        {
            double x = X(t), ty = ov ? OvSpeedH + 3 : y1 - 7;
            dc.DrawLine(new Pen(Brushes.White, 1), new Point(x, ty), new Point(x, ty + 7));
            double ly = ov ? ty + 11 : y1 - 10;
            DrawText(dc, label, x + 4, ly + 1, 9, Brushes.Black, Ui, vCenter: true);
            DrawText(dc, label, x + 3, ly, 9, Brushes.White, Ui, vCenter: true);
        }

        if (act && ed.ZoomFactor > 1.05)                                      // overview bar
        {
            double oy0 = ov ? ed.OverviewTop : tlb - 7, oy1 = ov ? tlb : tlb - 2;
            dc.DrawRectangle(Frozen("#444444"), null, R(Lx, oy0, Rx, oy1));
            dc.DrawRectangle(Frozen("#BBBBBB"), null, R(Lx + (Rx - Lx) * ed.ViewA / dur, oy0,
                                                        Lx + (Rx - Lx) * (ed.ViewA + ed.ViewLen) / dur, oy1));
        }

        if (dropX is { } dx)                                                  // where a dragged effect button would land
        {
            var pen = new Pen(Brushes.White, 2) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(dx, 0), new Point(dx, tlb));
        }

        if (act)                                                              // playhead
        {
            double x = Math.Max(-50, Math.Min(W0 + 50, X(ed.Playhead)));
            dc.DrawLine(new Pen(Yellow, 2), new Point(x, 0), new Point(x, tlb));
            dc.DrawGeometry(Yellow, null, Poly(new(x - 6, 0), new(x + 6, 0), new(x, 8)));
            double bx = Math.Min(Math.Max(x, ed.Pad + 32), Math.Max(ed.Pad + 32, W0 - ed.Pad - 32));
            double y = ov ? OvSpeedH + 6 + (ed.Ticks(W0).Any() ? 16 : 0) : ed.LaneHeight + 8;
            dc.DrawRectangle(Dark111, new Pen(Yellow, 1), R(bx - 32, y, bx + 32, y + 16));
            DrawText(dc, Text.FmtTime(ed.Playhead), bx, y + 8, 10.5, Yellow, UiBold, center: true, vCenter: true);
        }
    }

    static Rect R(double x0, double y0, double x1, double y1) => new(new Point(x0, y0), new Point(Math.Max(x0, x1), Math.Max(y0, y1)));

    static Pen RoundPen(string hex, double w) => new(Frozen(hex), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    static System.Windows.Media.Geometry Poly(params Point[] p)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(p[0], true, true);
            c.PolyLineTo(p[1..], true, false);
        }
        g.Freeze();
        return g;
    }

    void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, Typeface tf, bool center = false, bool vCenter = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush,
                                   VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(center ? x - ft.Width / 2 : x, vCenter ? y - ft.Height / 2 : y));
    }

    // ------------------------------------------------------------------ text boxes (trim times, multipliers)
    static TextBox TimeBox(string hex) => new()
    {
        Width = 60, Height = 20, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0), FontSize = 11, FontWeight = FontWeights.Bold,
        TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        Background = Frozen(hex), Foreground = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = Frozen(hex),
        CaretBrush = Brushes.White, Visibility = Visibility.Collapsed, Template = PlainBoxTemplate(),
    };

    /// <summary>A bare text box (the Fluent template has a minimum size and an underline that do not fit in 20 px).</summary>
    static ControlTemplate PlainBoxTemplate()
    {
        var t = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(host);
        t.VisualTree = border;
        return t;
    }

    void HookTimeBox(TextBox box, bool start)
    {
        box.GotKeyboardFocus += (_, _) =>
        {
            Host?.StopPlayback();
            if (vm is not null) Host?.SetActive(vm);
            box.Dispatcher.BeginInvoke(box.SelectAll);
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (Ed is null) return;
            if (e.Key == Key.Enter) { box.Text = Ed.CommitTrimText(start, box.Text); Focus(); Changed(); e.Handled = true; }
            else if (e.Key == Key.Escape) { box.Text = Text.FmtTime(start ? Ed.Clip.Start : Ed.Clip.End); Focus(); e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (Ed is null) return;
            box.Text = Ed.CommitTrimText(start, box.Text);
            Changed();
        };
    }

    TextBox SpeedBox(SpeedSection sec)
    {
        if (speedBoxes.TryGetValue(sec, out var b)) return b;
        b = new TextBox
        {
            Width = 54, Height = 20, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0), FontSize = 12, FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1),
            Template = PlainBoxTemplate(), Text = TimelineEditor.SectionText(sec),
            ToolTip = sec.IsPause ? "Length of this pause in seconds (the picture stops on this frame, then the clip goes on). Enter confirms."
                                  : "Speed of this section: 2 = twice as fast, 0.5 = half speed (0.1 - 100). Enter confirms.",
        };
        b.GotKeyboardFocus += (_, _) =>
        {
            if (vm is null || Ed is null) return;
            Ed.Sel = sec;
            Host?.StopPlayback();
            Host?.SetActive(vm);
            b.Dispatcher.BeginInvoke(b.SelectAll);
            Redraw();
        };
        b.PreviewKeyDown += (_, e) =>
        {
            if (Ed is null) return;
            if (e.Key == Key.Enter) { b.Text = Ed.CommitSpeedText(sec, b.Text); Focus(); Changed(); e.Handled = true; }
            else if (e.Key == Key.Escape) { b.Text = TimelineEditor.SectionText(sec); Focus(); e.Handled = true; }
        };
        b.LostKeyboardFocus += (_, _) => { if (Ed is not null) { b.Text = Ed.CommitSpeedText(sec, b.Text); Changed(); } };
        speedBoxes[sec] = b;
        Children.Add(b);
        return b;
    }

    /// <summary>Positions the text boxes like the reference create_window calls (they are real controls on top).</summary>
    void SyncBoxes()
    {
        if (Ed is not { } ed) return;
        double W0 = W, Lx = ed.Pad, Rx = W0 - ed.Pad;
        var vis = ed.Vis();
        foreach (var dead in speedBoxes.Keys.Where(s => !ed.Clip.Sections.Contains(s)).ToList())
        {
            Children.Remove(speedBoxes[dead]);
            speedBoxes.Remove(dead);
        }
        foreach (var (sec, box) in speedBoxes) box.Visibility = Visibility.Collapsed;
        if (W0 < 2 * ed.Pad + 2) return;
        foreach (var (sec, a, b) in vis)
        {
            if (!ed.Active) continue;
            double xa = ed.X(a, W0), xb = ed.X(b, W0);
            if (xb < Lx - 6 || xa > Rx + 6) continue;
            double ca = Math.Min(Math.Max(xa, Lx), Rx), cb = Math.Min(Math.Max(xb, Lx), Rx);
            var box = SpeedBox(sec);
            bool slow = sec.Speed < 1;
            string bg = sec.IsPause ? "#FFEDD5" : slow ? "#FFE0EF" : "#DFF0FF", fg = sec.IsPause ? "#4A1D0B" : slow ? "#4A0B2A" : "#0B2A4A";
            box.Background = Frozen(bg);
            box.Foreground = box.CaretBrush = Frozen(fg);
            box.BorderBrush = Frozen(fg);
            if (!box.IsKeyboardFocusWithin) box.Text = TimelineEditor.SectionText(sec);
            double cx = Math.Min(Math.Max((ca + cb) / 2, ed.Pad + 30), Math.Max(ed.Pad + 30, W0 - ed.Pad - 30));
            if (ed.Overlay)                                  // the short 2.0 header: a smaller box
            {
                (box.Width, box.Height, box.FontSize) = (40, 14, 10);
                SetLeft(box, cx - 20);
                SetTop(box, 2);
            }
            else
            {
                (box.Width, box.Height, box.FontSize) = (54, 20, 12);
                SetLeft(box, cx - 27);
                SetTop(box, LaneH / 2 - 10);
            }
            box.Visibility = Visibility.Visible;
        }
        double dur = ed.Dur;
        foreach (var (box, start, moved) in new[] { (startBox, true, ed.Clip.Start > 0.005), (endBox, false, ed.Clip.End < dur - 0.005) })
        {
            double xx = ed.X(start ? ed.Clip.Start : ed.Clip.End, W0);
            if (!ed.Active || !moved || !(Lx - 6 <= xx && xx <= Rx + 6)) { box.Visibility = Visibility.Collapsed; continue; }
            if (!box.IsKeyboardFocusWithin) box.Text = Text.FmtTime(start ? ed.Clip.Start : ed.Clip.End);
            int d = start ? -1 : 1;
            double ew = 60, cx = xx + d * (3 + 5 + ew / 2);
            if (cx - ew / 2 < 1 || cx + ew / 2 > W0 - 1) cx = xx - d * (3 + 5 + ew / 2);   // no room outside: inside
            double y0 = ed.StripTop, y1 = ed.Height;
            SetLeft(box, cx - ew / 2);
            SetTop(box, (y0 + y1) / 2 - 10);
            box.Visibility = Visibility.Visible;
        }
    }

    void Changed() => vm?.NotifyChanged();

    // ------------------------------------------------------------------ mouse
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (vm is null || Ed is not { } ed || Host is not { } host) return;
        var p = e.GetPosition(this);
        host.StopPlayback();
        bool wasCompact = !ed.Active;
        var mods = Keyboard.Modifiers;
        if ((mods & (ModifierKeys.Shift | ModifierKeys.Control)) != 0 && wasCompact)   // only changes the selection
        {
            host.ClickRow(vm, mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Control));
            e.Handled = true;
            return;
        }
        if (wasCompact || host.SelectionCount > 1) host.ClickRow(vm, false, false);
        Focus();
        ed.Down(p.X, p.Y, W, wasCompact);
        Cursor = AppCursors.ForTimeline(ed.DragHover, true);
        CaptureMouse();
        Changed();
        e.Handled = true;
    }

    /// <summary>The timeline is the bottom of the clip card: its lower corners follow the card's rounding
    /// (radius 9 minus the 2 px border), so the thumbnails reach the border without poking out of the corners.</summary>
    void UpdateClip()
    {
        double w = ActualWidth, h = ActualHeight, r = vm?.SoundLaneHeight > 0 ? 0 : 7;   // (2.7: the Sounds lane is the bottom then)
        if (r == 0) { Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, w), Math.Max(0, h))); return; }
        if (w < 2 * r || h < 2 * r) { Clip = null; return; }
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
        Clip = g;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Ed is not { } ed) return;
        var p = e.GetPosition(this);
        if (ed.Drag == DragKind.None || e.LeftButton != MouseButtonState.Pressed)
        {
            // 2.7.5: the left-right arrow over edges, the hand over section headers and the overview bar
            Cursor = AppCursors.ForTimeline(ed.Hover(p.X, p.Y, W), false);
            return;
        }
        Cursor = AppCursors.ForTimeline(ed.DragHover, true);            // (a closed hand while a header is dragged)
        ed.Move(p.X, W);
        Changed();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Ed is not { Drag: not DragKind.None }) Cursor = null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Ed?.Up();
        ReleaseMouseCapture();
        if (Ed is { } ed) Cursor = AppCursors.ForTimeline(ed.Hover(e.GetPosition(this).X, e.GetPosition(this).Y, W), false);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Ed?.Up();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Ed is not { } ed) return;
        var p = e.GetPosition(this);
        if (ed.RightClick(p.X, p.Y, W)) Changed();
        e.Handled = true;
    }

    // ------------------------------------------------------------------ effect buttons dropped on the timeline
    protected override void OnDragEnter(DragEventArgs e) => DragHover(e);
    protected override void OnDragOver(DragEventArgs e) => DragHover(e);

    void DragHover(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(EffectFormat) || Ed is null) return;
        e.Effects = DragDropEffects.Copy;
        dropX = e.GetPosition(this).X;
        InvalidateVisual();
        e.Handled = true;                                      // (the window would refuse anything but video files)
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
        if (e.Data.GetData(EffectFormat) is not string act || vm is null || Ed is not { } ed || Host is not { } host) return;
        host.AddEffectAt(vm, act, ed.T(e.GetPosition(this).X, W));
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Ed is not { } ed) return;
        if (ed.Wheel(e.GetPosition(this).X, e.Delta / 120.0, Keyboard.Modifiers.HasFlag(ModifierKeys.Control), W))
        {
            Changed();
            e.Handled = true;                                   // otherwise the clip list scrolls
        }
    }
}
