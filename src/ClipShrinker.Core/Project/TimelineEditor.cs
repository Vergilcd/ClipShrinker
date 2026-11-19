using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Project;

/// <summary>What the mouse is dragging on a timeline.</summary>
public enum DragKind { None, Play, Start, End, SecA, SecB, SecMove, ZoomA, ZoomB, ZoomMove, Overview }

/// <summary>
/// The editing logic of one clip's timeline, without any UI (reference ClipRow lines 2186-3221): trim handles,
/// playhead, speed and zoom sections, hit-testing, dragging with snapping, timeline zoom / scroll, and the
/// thumbnail layout. Coordinates are in pixels of a timeline that is W wide; the view draws what this describes.
/// </summary>
public sealed class TimelineEditor
{
    // geometry of the timeline (pixels), as in the reference
    public const double TlH = 133, LaneH = 30, ZLaneH = 18, CompactTlH = 42, CompactLaneH = 4;
    public const double HandleGrab = 10, ThumbH = 72, SnapPx = 9;

    /// <summary>Space left and right of the video on the timeline (the reference uses 14 px; 2.0 draws edge to edge).</summary>
    public double Pad { get; init; } = 14;

    /// <summary>Height of the active timeline (the reference uses 133 px with a gap under the thumbnails).</summary>
    public double ActiveHeight { get; init; } = TlH;

    /// <summary>Height of an inactive clip's thin strip (the reference: 42 px; 2.1 draws it 25 % higher).</summary>
    public double CompactHeight { get; init; } = CompactTlH;

    /// <summary>Height of the thumbnails (the reference: 72 px, under the lanes).</summary>
    public double ThumbHeight { get; init; } = ThumbH;

    /// <summary>2.0 layout: no lanes above the thumbnails. The speed section headers lie on top of the thumbnails,
    /// the zoom headers at the bottom, the overview bar (when zoomed in) under them.</summary>
    public bool Overlay { get; init; }

    public const double OvSpeedH = 16, OvZoomH = 14, OvOverviewH = 6;

    /// <summary>The 2.0 look: thumbnails from edge to edge, filling the whole timeline, with the effect headers
    /// on top of them (same height as before: 124 px).</summary>
    public static TimelineEditor EdgeToEdge(TimelineClip clip)
    {
        const double h = LaneH + ZLaneH + 4 + ThumbH;
        return new TimelineEditor(clip) { Pad = 0, ActiveHeight = h, ThumbHeight = h, Overlay = true, CompactHeight = 52 };
    }

    /// <summary>Top of the overview bar (overlay layout, only while zoomed in).</summary>
    public double OverviewTop => Height - OvOverviewH;

    /// <summary>The zoom header band (overlay layout): at the bottom, above the overview bar while zoomed in.</summary>
    public (double Top, double Bottom) ZoomLane
    {
        get
        {
            double bottom = ZoomFactor > 1.05 ? OverviewTop - 1 : Height;
            return (bottom - OvZoomH, bottom);
        }
    }

    /// <summary>The thumbnail strip: under the lanes in the reference layout, the whole timeline in the overlay layout.</summary>
    public double StripTop => Overlay ? 0 : LaneHeight + 4;

    bool InZoomLane(double y)
    {
        if (!Overlay) return LaneH <= y && y < LaneHeight;
        var (top, bottom) = ZoomLane;
        return Active && top - 2 <= y && y < bottom + 1;
    }

    bool InSpeedLane(double y) => Overlay ? y < OvSpeedH + 2 : y < LaneHeight;

    bool InOverview(double y) => ZoomFactor > 1.05 && (Overlay ? y >= OverviewTop - 1 : y >= Height - 10);

    public TimelineClip Clip { get; }
    public double Dur => Clip.Info.Dur;
    public double Playhead { get; set; }
    public double ViewA { get; private set; }
    public double ViewLen { get; private set; }
    public SpeedSection? Sel { get; set; }
    public ZoomSection? ZSel { get; set; }
    public bool Active { get; set; }
    public DragKind Drag { get; private set; }

    /// <summary>A status message for the user (the reference set_status).</summary>
    public Action<string> Status { get; set; } = _ => { };
    /// <summary>Called before a change that undo should be able to take back (wired up with undo in a later phase).</summary>
    public Action<string> PushUndo { get; set; } = _ => { };
    /// <summary>The view should show a frame at this time (reference request_preview).</summary>
    public Action<double> PreviewRequested { get; set; } = _ => { };
    /// <summary>The visible part changed: thumbnails for it are needed (the reference schedules refresh_thumbs).</summary>
    public Action ViewChanged { get; set; } = () => { };

    object? dragTarget;               // the section / zoom being dragged
    double dragOffset;                // pointer offset inside a moved section
    double? snapT;

    public TimelineEditor(TimelineClip clip)
    {
        Clip = clip;
        ViewA = 0;
        ViewLen = Math.Max(clip.Info.Dur, 0.1);
    }

    public double LaneHeight => Active ? LaneH + ZLaneH : CompactLaneH;
    /// <summary>Height of the canvas; also the bottom of the drawing area.</summary>
    public double Height => Active ? ActiveHeight : CompactHeight;

    // ------------------------------------------------------------------ mapping and zoom
    public double X(double t, double W) => Pad + (W - 2 * Pad) * (t - ViewA) / ViewLen;

    public double T(double x, double W)
    {
        double w = Math.Max(1, W - 2 * Pad);
        return Min(Max(ViewA + (x - Pad) / w * ViewLen, 0), Dur);
    }

    public double ZoomFactor => Dur / Math.Max(ViewLen, 1e-6);
    public bool Zoomed => ViewLen < Dur - 1e-6;

    /// <summary>Level of thumbnail detail: each level has twice as many thumbnails.</summary>
    public int Level => (int)Math.Max(0, Math.Ceiling(Math.Log2(Math.Max(ZoomFactor, 1.0)) - 0.01));

    public bool SetView(double a, double length)
    {
        length = Min(Max(length, Min(C.MinView, Dur)), Dur);
        a = Min(Max(a, 0.0), Dur - length);
        if (Math.Abs(a - ViewA) < 1e-9 && Math.Abs(length - ViewLen) < 1e-9) return false;
        ViewA = a;
        ViewLen = length;
        ViewChanged();
        return true;
    }

    /// <summary>Sets the view without the limits of SetView (tests, restoring a saved view).</summary>
    public void ForceView(double a, double length)
    {
        ViewA = a;
        ViewLen = length;
    }

    public void ZoomAt(double x, double factor, double W)
    {
        double w = Math.Max(1, W - 2 * Pad);
        double frac = Min(Max((x - Pad) / w, 0.0), 1.0);
        double tm = ViewA + frac * ViewLen;
        double newLen = ViewLen / factor;
        SetView(tm - frac * newLen, newLen);
    }

    public void Fit() => SetView(0.0, Dur);

    /// <summary>Mouse wheel: Ctrl = zoom at the pointer; zoomed in = scroll. False = not handled (the list scrolls).</summary>
    public bool Wheel(double x, double steps, bool ctrl, double W)
    {
        if (ctrl) { ZoomAt(x, Math.Pow(1.35, steps), W); return true; }
        if (Zoomed) { SetView(ViewA - steps * 0.2 * ViewLen, ViewLen); return true; }
        return false;
    }

    /// <summary>"zoom x2.5" while zoomed in.</summary>
    public string ZoomLabel => ZoomFactor > 1.02 ? $"zoom x{F(ZoomFactor, 1)}" : "";

    /// <summary>Time marks while zoomed in: positions and labels.</summary>
    public IEnumerable<(double T, string Label)> Ticks(double W)
    {
        if (!(Active && ZoomFactor > 1.05)) yield break;
        double pps = (W - 2 * Pad) / ViewLen;
        double step = C.TickSteps.FirstOrDefault(st => st * pps >= 90, C.TickSteps[^1]);
        for (double tt = Math.Ceiling(ViewA / step) * step; tt <= ViewA + ViewLen + 1e-6; tt += step)
            yield return (tt, Text.FmtTick(tt));
    }

    // ------------------------------------------------------------------ sections
    /// <summary>Speed sections clipped to the trim (only those still visible), by start time.</summary>
    public List<(SpeedSection Sec, double A, double B)> Vis()
    {
        var res = new List<(SpeedSection, double, double)>();
        if (!Modules.Features.TimelineEffects) return res;  // module off: the sections are kept but not shown
        foreach (var s in Clip.Sections.OrderBy(x => x.A))
        {
            double a = Max(s.A, Clip.Start), b = Min(s.B, Clip.End);
            if (b - a >= 0.05) res.Add((s, a, b));
        }
        return res;
    }

    /// <summary>Zoom sections clipped to the trim.</summary>
    public List<(ZoomSection Z, double A, double B)> VisZ()
    {
        var res = new List<(ZoomSection, double, double)>();
        if (!Modules.Features.TimelineEffects) return res;
        foreach (var z in Clip.Zooms.OrderBy(x => x.A))
        {
            double a = Max(z.A, Clip.Start), b = Min(z.B, Clip.End);
            if (b - a >= 0.05) res.Add((z, a, b));
        }
        return res;
    }

    static (double PrevB, double NextA) Bounds(double secA, IEnumerable<(double A, double B)> others, double start, double end)
    {
        var o = others.ToList();
        double prevB = o.Where(x => x.A < secA).Select(x => x.B).Append(start).Aggregate(Max);
        double nextA = o.Where(x => x.A > secA).Select(x => x.A).Append(end).Aggregate(Min);
        return (prevB, nextA);
    }

    (double, double) Neighbors(SpeedSection sec) =>
        Bounds(sec.A, Vis().Where(v => !ReferenceEquals(v.Sec, sec)).Select(v => (Max(v.Sec.A, Clip.Start), Min(v.Sec.B, Clip.End))), Clip.Start, Clip.End);

    (double, double) ZNeighbors(ZoomSection z) =>
        Bounds(z.A, VisZ().Where(v => !ReferenceEquals(v.Z, z)).Select(v => (Max(v.Z.A, Clip.Start), Min(v.Z.B, Clip.End))), Clip.Start, Clip.End);

    /// <summary>Length of the clip after trimming and the speed sections.</summary>
    public double EditedLen
    {
        get
        {
            var v = Vis();
            return v.Count > 0 ? Timing.Duration(Timing.BuildPieces(Clip.Start, Clip.End, v.Select(x => x.Sec))) : Clip.End - Clip.Start;
        }
    }

    /// <summary>Hotkeys 1 / 2: the trim start / end goes to the playhead.</summary>
    public bool SetTrim(bool start)
    {
        double t = Min(Max(Playhead, 0.0), Dur);
        if (start)
        {
            if (t > Clip.End - 0.5) { Status("Trim start must be at least 0.5 s before the end marker"); return false; }
            PushUndo("trim start");
            Clip.Start = t;
        }
        else
        {
            if (t < Clip.Start + 0.5) { Status("Trim end must be at least 0.5 s after the start marker"); return false; }
            PushUndo("trim end");
            Clip.End = t;
        }
        Status($"Trim {(start ? "start" : "end")} set to {Text.FmtTime(t)}");
        return true;
    }

    /// <summary>A time typed into a trim box (Enter or leaving it). Returns the text the box shows afterwards.</summary>
    public string CommitTrimText(bool start, string text)
    {
        var t0 = Text.ParseClock(text);
        double cur = start ? Clip.Start : Clip.End;
        if (t0 is { } t)
        {
            t = Min(Max(t, 0.0), Dur);
            t = start ? Min(t, Clip.End - 0.5) : Max(t, Clip.Start + 0.5);
            t = Min(Max(t, 0.0), Dur);
            if (Math.Abs(t - cur) > 1e-3)
            {
                PushUndo("trim " + (start ? "start" : "end"));
                if (start) { Clip.Start = t; Playhead = t; }
                else { Clip.End = t; Playhead = Max(Clip.Start, t - 0.1); }
                PreviewRequested(Playhead);
                Status($"Trim {(start ? "start" : "end")} set to {Text.FmtTime(t)}");
            }
        }
        return Text.FmtTime(start ? Clip.Start : Clip.End);
    }

    /// <summary>The first free place for a new section at / after the playhead (reference add_section / add_zoom).</summary>
    (double G0, double G1)? FreeGap(IEnumerable<(double A, double B)> busyIn)
    {
        double lo = Clip.Start, hi = Clip.End;
        double t = lo <= Playhead && Playhead < hi ? Playhead : lo;
        var busy = busyIn.ToList();
        foreach (var (a, b) in busy)                          // playhead inside a section -> start after it
            if (a - 1e-6 <= t && t < b) t = b;
        var free = new List<(double, double)>();
        double pos = lo;
        foreach (var (a, b) in busy)
        {
            if (a - pos >= C.MinSection) free.Add((pos, a));
            pos = Max(pos, b);
        }
        if (hi - pos >= C.MinSection) free.Add((pos, hi));
        foreach (var (g0, g1) in free)
            if (g1 - Max(g0, t) >= C.MinSection) return (Max(g0, t), g1);
        return free.Count > 0 ? free[0] : null;
    }

    /// <summary>Hotkey S / D: a new speed section (D: slow motion) at the playhead or in the next free gap.</summary>
    public SpeedSection? AddSection(double speed = 2.0)
    {
        if (!Modules.Features.TimelineEffects) return null;
        if (FreeGap(Vis().Select(v => (v.A, v.B))) is not { } gap)
        {
            Status("No free space for a speed section - trim handles / other sections are in the way");
            return null;
        }
        var (g0, g1) = gap;
        double length = Min(Max(1.0, 0.1 * Min(Clip.End - Clip.Start, ViewLen)), g1 - g0);
        var sec = new SpeedSection(g0, g0 + length, speed);
        PushUndo("add speed section");
        Clip.Sections.Add(sec);
        Sel = sec;
        ZSel = null;
        Playhead = sec.A;
        PreviewRequested(Playhead);
        Status(speed < 1 ? "Slow-motion section added - type a multiplier in the pink box (e.g. 0.25 = four times slower)"
                         : "Speed section added - type a multiplier in the blue box (e.g. 4 = four times faster)");
        return sec;
    }

    /// <summary>Hotkey Z: a new zoom section (white) at the playhead or in the next free gap.</summary>
    public ZoomSection? AddZoom()
    {
        if (!Modules.Features.TimelineEffects) return null;
        if (FreeGap(VisZ().Select(v => (v.A, v.B))) is not { } gap)
        {
            Status("No free space for a zoom section - trim handles / other zoom sections are in the way");
            return null;
        }
        var (g0, g1) = gap;
        double length = Min(Max(2.0, 0.1 * Min(Clip.End - Clip.Start, ViewLen)), g1 - g0);
        PushUndo("add zoom section");
        var z = new ZoomSection(g0, g0 + length, 0.25, 0.25, 0.5);
        Clip.Zooms.Add(z);
        (ZSel, Sel) = (z, null);
        Playhead = z.A;
        PreviewRequested(Playhead);
        Status("Zoom section added - drag the white bracket on the preview to choose the area (corners = size)");
        return z;
    }

    public bool RemoveSection(SpeedSection? sec)
    {
        if (sec is null || !Modules.Features.TimelineEffects || !Clip.Sections.Contains(sec)) return false;
        PushUndo("remove speed section");
        Clip.Sections.Remove(sec);
        if (ReferenceEquals(Sel, sec)) Sel = null;
        return true;
    }

    public bool RemoveZoom(ZoomSection? z)
    {
        if (z is null || !Modules.Features.TimelineEffects || !Clip.Zooms.Contains(z)) return false;
        PushUndo("remove zoom section");
        Clip.Zooms.Remove(z);
        if (ReferenceEquals(ZSel, z)) ZSel = null;
        return true;
    }

    /// <summary>"4x" etc. as the speed box shows it.</summary>
    public static string SpeedText(double speed) => G(speed) + "x";

    /// <summary>A multiplier typed into a speed box; invalid text keeps the old value. Returns the new box text.</summary>
    public string CommitSpeedText(SpeedSection sec, string text)
    {
        string txt = text.Trim().ToLowerInvariant().Replace(",", ".").Replace("×", "").Replace("x", "").Trim();
        if (ParseFloat(txt) is { } v && v > 0)
        {
            if (Math.Abs(Text.ClampSpeed(v) - sec.Speed) > 1e-9) PushUndo("speed multiplier");
            sec.Speed = Text.ClampSpeed(v);
        }
        return SpeedText(sec.Speed);
    }

    /// <summary>Right-click: removes the zoom (zoom lane) or speed section under the pointer.</summary>
    public bool RightClick(double x, double y, double W)
    {
        double t = T(x, W);
        if (Active && InZoomLane(y))
            foreach (var (z, a, b) in VisZ())
                if (a <= t && t <= b) return RemoveZoom(z);
        foreach (var (sec, a, b) in Vis())
            if (a <= t && t <= b) return RemoveSection(sec);
        return false;
    }

    // ------------------------------------------------------------------ dragging
    /// <summary>Mouse button pressed on the timeline (reference on_down, for a clip that is already active,
    /// or wasCompact = the thin strip of another clip: the first click only selects and seeks).</summary>
    public void Down(double x, double y, double W, bool wasCompact = false)
    {
        dragTarget = null;
        if (wasCompact)
        {
            Drag = DragKind.Play;
            Move(x, W);
            return;
        }
        if (InOverview(y))                                   // overview bar: drag to scroll
        {
            Drag = DragKind.Overview;
            Move(x, W);
            return;
        }
        double t = T(x, W);
        if (InZoomLane(y))                                   // zoom lane
        {
            var zc = new List<(double D, DragKind K, ZoomSection Z)>();
            foreach (var (z, a, b) in VisZ())
            {
                zc.Add((Math.Abs(x - X(a, W)), DragKind.ZoomA, z));
                zc.Add((Math.Abs(x - X(b, W)), DragKind.ZoomB, z));
            }
            var best = zc.Count > 0 ? zc.Aggregate((p, q) => q.D < p.D ? q : p) : default;
            var hit = VisZ().FirstOrDefault(v => v.A <= t && t <= v.B);
            if (zc.Count > 0 && best.D <= HandleGrab)
            {
                Drag = best.K;
                dragTarget = best.Z;
                (best.Z.A, best.Z.B) = (Max(best.Z.A, Clip.Start), Min(best.Z.B, Clip.End));
                (ZSel, Sel) = (best.Z, null);
            }
            else if (hit.Z is not null)
            {
                (hit.Z.A, hit.Z.B) = (hit.A, hit.B);
                (ZSel, Sel) = (hit.Z, null);
                Drag = DragKind.ZoomMove;
                dragTarget = hit.Z;
                dragOffset = t - hit.A;
            }
            else
            {
                (ZSel, Sel) = (null, null);
                Drag = DragKind.Play;
            }
            if (Drag != DragKind.Play) PushUndo("zoom section");
            SnapSetup();
            Move(x, W);
            return;
        }
        bool inLane = InSpeedLane(y);
        var cands = new List<(double D, DragKind K, object? Target)>
        {
            (Math.Abs(x - X(Clip.Start, W)) + (inLane ? 6 : 0), DragKind.Start, null),
            (Math.Abs(x - X(Clip.End, W)) + (inLane ? 6 : 0), DragKind.End, null),
        };
        var vis = Vis();
        foreach (var (sec, a, b) in vis)
        {
            cands.Add((Math.Abs(x - X(a, W)), DragKind.SecA, sec));
            cands.Add((Math.Abs(x - X(b, W)), DragKind.SecB, sec));
        }
        foreach (var (z, a, b) in VisZ())                    // the zoom bars run through the strip too
        {
            cands.Add((Math.Abs(x - X(a, W)) - 0.5, DragKind.ZoomA, z));
            cands.Add((Math.Abs(x - X(b, W)) - 0.5, DragKind.ZoomB, z));
        }
        var pick = cands.Aggregate((p, q) => q.D < p.D ? q : p);
        Drag = DragKind.None;
        if (pick.D <= HandleGrab)
        {
            Drag = pick.K;
            dragTarget = pick.Target;
            if (pick.Target is SpeedSection s)
            {
                (s.A, s.B) = (Max(s.A, Clip.Start), Min(s.B, Clip.End));
                (Sel, ZSel) = (s, null);
            }
            else if (pick.Target is ZoomSection z)
            {
                (z.A, z.B) = (Max(z.A, Clip.Start), Min(z.B, Clip.End));
                (ZSel, Sel) = (z, null);
            }
        }
        else
        {
            var hit = vis.FirstOrDefault(v => v.A <= t && t <= v.B);
            Sel = hit.Sec;
            if (hit.Sec is not null) ZSel = null;
            else if (Overlay) ZSel = VisZ().FirstOrDefault(v => v.A <= t && t <= v.B).Z;   // 2.0: a click inside a zoom area selects it, like a speed section
            if (hit.Sec is not null && inLane)
            {
                (hit.Sec.A, hit.Sec.B) = (hit.A, hit.B);
                Drag = DragKind.SecMove;
                dragTarget = hit.Sec;
                dragOffset = t - hit.A;
            }
            else Drag = DragKind.Play;
        }
        if (Drag == DragKind.None) Drag = DragKind.Play;
        if (Drag != DragKind.Play)
            PushUndo(Drag is DragKind.Start or DragKind.End ? "trim" : Drag is DragKind.ZoomA or DragKind.ZoomB ? "zoom section" : "speed section");
        SnapSetup();
        Move(x, W);
    }

    /// <summary>True when a press at (x, y) would grab something rather than move the playhead: a trim handle, a
    /// section edge or bar, a zoom section or the overview bar (the same rules as Down). Used for the hand cursor.</summary>
    public bool Grabbable(double x, double y, double W)
    {
        if (!Active) return false;
        if (InOverview(y)) return true;
        double t = T(x, W);
        bool Near(double tt, double extra = 0) => Math.Abs(x - X(tt, W)) + extra <= HandleGrab;
        if (InZoomLane(y))
            return VisZ().Any(v => Near(v.A) || Near(v.B) || (v.A <= t && t <= v.B));
        bool inLane = InSpeedLane(y);
        if (Near(Clip.Start, inLane ? 6 : 0) || Near(Clip.End, inLane ? 6 : 0)) return true;
        if (Vis().Any(v => Near(v.A) || Near(v.B) || (inLane && v.A <= t && t <= v.B))) return true;
        return VisZ().Any(v => Near(v.A, -0.5) || Near(v.B, -0.5));
    }

    /// <summary>The bars snap to the yellow line where playback was stopped (not to the bar's own position).</summary>
    void SnapSetup()
    {
        double pt = Playhead;
        double[] own = (Drag, dragTarget) switch
        {
            (DragKind.Start, _) => [Clip.Start],
            (DragKind.End, _) => [Clip.End],
            (DragKind.SecA, SpeedSection s) => [s.A],
            (DragKind.SecB, SpeedSection s) => [s.B],
            (DragKind.SecMove, SpeedSection s) => [s.A, s.B],
            (DragKind.ZoomMove, ZoomSection z) => [z.A, z.B],
            (DragKind.ZoomA, ZoomSection z) => [z.A],
            (DragKind.ZoomB, ZoomSection z) => [z.B],
            _ => [],
        };
        snapT = Drag is DragKind.Play or DragKind.Overview || own.Any(o => Math.Abs(pt - o) < 1e-3) ? null : pt;
    }

    /// <summary>t (and t + each offset) is pulled onto the snap position when it is within a few pixels.</summary>
    double Snap(double t, double W, params double[] offsets)
    {
        if (snapT is not { } st) return t;
        double pps = Math.Max(1, W - 2 * Pad) / ViewLen;
        foreach (double off in offsets.Prepend(0.0))
            if (Math.Abs(t + off - st) * pps <= SnapPx) return st - off;
        return t;
    }

    /// <summary>Mouse moved with the button down (reference on_move).</summary>
    public void Move(double x, double W)
    {
        if (Drag == DragKind.None) return;
        if (Drag == DragKind.Overview)
        {
            double w = Math.Max(1, W - 2 * Pad);
            SetView((x - Pad) / w * Dur - ViewLen / 2, ViewLen);
            return;
        }
        if (Zoomed && (x < Pad || x > W - Pad))              // edge scroll
            SetView(ViewA + (x < Pad ? -1 : 1) * 0.04 * ViewLen, ViewLen);
        double t = T(x, W);
        if (Drag is DragKind.Start or DragKind.End or DragKind.SecA or DragKind.SecB or DragKind.ZoomA or DragKind.ZoomB)
            t = Snap(t, W);
        switch (Drag)
        {
            case DragKind.Start:
                Clip.Start = Max(0.0, Min(t, Clip.End - 0.5));
                Playhead = Clip.Start;
                break;
            case DragKind.End:
                Clip.End = Min(Dur, Max(t, Clip.Start + 0.5));
                Playhead = Max(Clip.Start, Clip.End - 0.1);
                break;
            case DragKind.Play:
                Playhead = t;
                break;
            case DragKind.ZoomA or DragKind.ZoomB or DragKind.ZoomMove when dragTarget is ZoomSection z:
            {
                var (lo, hi) = ZNeighbors(z);
                if (Drag == DragKind.ZoomA) { z.A = Min(Max(t, lo), z.B - C.MinSection); Playhead = z.A; }
                else if (Drag == DragKind.ZoomB) { z.B = Max(Min(t, hi), z.A + C.MinSection); Playhead = Max(z.A, z.B - 0.05); }
                else
                {
                    double w = z.B - z.A;
                    if (hi - lo >= w)
                    {
                        z.A = Min(Max(Snap(t - dragOffset, W, w), lo), hi - w);
                        z.B = z.A + w;
                    }
                    Playhead = z.A;
                }
                break;
            }
            case DragKind.SecA or DragKind.SecB or DragKind.SecMove when dragTarget is SpeedSection s:
            {
                var (lo, hi) = Neighbors(s);
                if (Drag == DragKind.SecA) { s.A = Min(Max(t, lo), s.B - C.MinSection); Playhead = s.A; }
                else if (Drag == DragKind.SecB) { s.B = Max(Min(t, hi), s.A + C.MinSection); Playhead = Max(s.A, s.B - 0.05); }
                else
                {
                    double w = s.B - s.A;
                    if (hi - lo >= w)
                    {
                        s.A = Min(Max(Snap(t - dragOffset, W, w), lo), hi - w);
                        s.B = s.A + w;
                    }
                    Playhead = s.A;
                }
                break;
            }
        }
        PreviewRequested(Playhead);
    }

    public void Up()
    {
        Drag = DragKind.None;
        dragTarget = null;
    }

    /// <summary>Playback moves the playhead; a zoomed view follows it (reference set_playhead_fast).</summary>
    public void FollowPlayhead(double t)
    {
        Playhead = t;
        if (Zoomed && !(ViewA <= t && t <= ViewA + ViewLen)) SetView(t - 0.1 * ViewLen, ViewLen);
    }

    // ------------------------------------------------------------------ thumbnails
    /// <summary>Width of one thumbnail (72 px high, the clip's aspect ratio).</summary>
    public int ThumbWidth
    {
        get
        {
            double k = ThumbHeight / ThumbH;                    // the reference limits (48 - 240 px) grow with the height
            return (int)Math.Max(48 * k, Math.Min(240 * k, (int)(ThumbHeight * Clip.Info.W / (double)Clip.Info.H)));
        }
    }

    /// <summary>How many thumbnails side by side fill the timeline at this width (level 0).</summary>
    public int WantN1(double W) => Math.Max(5, (int)Math.Ceiling((Math.Max(W, 800) - 2 * Pad) / ThumbWidth));

    /// <summary>Thumbnail k of level L (n1 at level 0): its centre time.</summary>
    public double ThumbTime(int level, int k, int n1) => (k + 0.5) * Dur / (n1 * Math.Pow(2, level));

    /// <summary>The thumbnails needed for the visible part (plus a quarter on each side), nearest to the centre first.</summary>
    public List<int> NeededThumbs(int n1, Func<int, bool> have)
    {
        int L = Level;
        int count = n1 * (1 << L);
        double step = Dur / count;
        int lo = (int)Math.Max(0, Math.Floor((ViewA - 0.25 * ViewLen) / step));
        int hi = (int)Math.Min(count - 1, Math.Ceiling((ViewA + 1.25 * ViewLen) / step));
        double mid = ViewA + ViewLen / 2;
        return Enumerable.Range(lo, Math.Max(0, hi - lo + 1)).Where(k => !have(k)).OrderBy(k => Math.Abs((k + 0.5) * step - mid)).ToList();
    }

    /// <summary>The title line of the clip card.</summary>
    public string Title(int index) => TitleName(index) + "   -   " + TitleInfo;

    /// <summary>2.7.5: the title bar's first line (bold): number and file name.</summary>
    public string TitleName(int index) => $"{(Active ? "▶ " : "")}{index + 1}.  {Path.GetFileName(Clip.Path)}";

    /// <summary>2.7.5: the title bar's second line: the video's data and its length after editing.</summary>
    public string TitleInfo
    {
        get
        {
            var i = Clip.Info;
            return $"{i.W}x{i.H}, {F(i.Fps, 0)} fps, {F(i.Size / 1048576.0, 1)} MB, {Text.FmtTime(i.Dur)}" +
                   (i.Trc is not null ? "   [HDR]" : "") + (i.NAudio > 1 ? $"   [{i.NAudio} audio tracks]" : "") +
                   $"   -   Length after edit: {Text.FmtTime(EditedLen)}";
        }
    }
}
