using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Project;

/// <summary>The zoom area on the preview (reference _br_move): move it, or resize it from a corner while the
/// opposite corner stays. x, y, w are fractions of the picture; the area is square in picture fractions.</summary>
public static class ZoomArea
{
    /// <summary>Moved by (dx, dy) picture fractions from where the drag started.</summary>
    public static (double X, double Y) Move(double ox, double oy, double ow, double dx, double dy) =>
        (Min(Max(ox + dx, 0.0), 1 - ow), Min(Max(oy + dy, 0.0), 1 - ow));

    /// <summary>Corner "nw" / "ne" / "sw" / "se" dragged to (fx, fy) (picture fractions).</summary>
    public static (double X, double Y, double W) Resize(string corner, double ox, double oy, double ow, double fx, double fy)
    {
        int sx = corner[1] == 'e' ? 1 : -1, sy = corner[0] == 's' ? 1 : -1;
        double ax = sx > 0 ? ox : ox + ow, ay = sy > 0 ? oy : oy + ow;        // the opposite corner stays
        double w = Max(sx * (fx - ax), sy * (fy - ay));
        double wmax = Min(sx > 0 ? 1 - ax : ax, sy > 0 ? 1 - ay : ay);
        w = Min(Max(w, C.ZoomMinW), wmax);
        return (sx > 0 ? ax : ax - w, sy > 0 ? ay : ay - w, w);
    }

    /// <summary>The zoom section the bracket edits: the selected one if the playhead is in it, else the first one
    /// under the playhead (reference _br_target). Null = no bracket.</summary>
    public static ZoomSection? Target(TimelineEditor ed)
    {
        double ph = ed.Playhead;
        var inside = ed.VisZ().Where(v => v.A - 1e-3 <= ph && ph <= v.B + 1e-3).Select(v => v.Z).ToList();
        if (inside.Count == 0) return null;
        return ed.ZSel is { } s && inside.Contains(s) ? s : inside[0];
    }
}

/// <summary>Sizes and settings of the preview (reference _pv_dims, start_playback, the AUD / ZOOM traces).</summary>
public static class PreviewPlan
{
    /// <summary>The preview frame size: the box, but at most 1600 x 900.</summary>
    public static (int W, int H) Dims(double boxW, double boxH, bool fullScreen = false)
    {
        if (boxW < 50 || boxH < 50) (boxW, boxH) = (640, 360);
        var (mw, mh) = fullScreen ? (C.FullPvW, C.FullPvH) : (C.MaxPvW, C.MaxPvH);
        return ((int)Math.Min(boxW, mw), (int)Math.Min(boxH, mh));
    }

    /// <summary>The crop of a clip for the preview, or null when the clip is already narrower than the ratio.</summary>
    public static (int W, int H)? Crop(MediaInfo i, double? ratio)
    {
        var (cw, ch) = Geometry.CropSize(i.W, i.H, ratio);
        return (cw, ch) != (i.W, i.H) ? (cw, ch) : null;
    }

    /// <summary>Playback frame size: the (cropped) picture fitted into the preview, never enlarged, even sizes.</summary>
    public static (int W, int H) PlaySize(MediaInfo i, double? ratio, (int W, int H) box)
    {
        var (cw, ch) = Geometry.CropSize(i.W, i.H, ratio);
        double scale = Min(Min(box.W / (double)cw, box.H / (double)ch), 1.0);
        return (Geometry.Even(cw * scale), Geometry.Even(ch * scale));
    }

    public static double PlayFps(MediaInfo i) => Min(C.PlayFpsCap, i.Fps);

    /// <summary>Where playback starts: at the playhead (if it is inside the trim and not right at the end), else at the start.</summary>
    public static double StartTime(TimelineClip c, double playhead) =>
        c.Start <= playhead && playhead < c.End - 0.25 ? playhead : c.Start;

    /// <summary>2.1: what Play does. Inside the trim it loops the trimmed part from the playhead (as before);
    /// with the playhead outside the trim it plays once from the playhead to the end of the video and stops.</summary>
    public static (double T0, double End, bool Once) PlayRange(TimelineClip c, double playhead)
    {
        double dur = c.Info.Dur;
        bool outside = playhead < c.Start || playhead >= c.End;
        if (outside && playhead < dur - 0.25) return (playhead, dur, true);
        return (StartTime(c, playhead), c.End, false);
    }

    /// <summary>The effect settings the preview uses: the current choices of the Effects window. An unreadable zoom
    /// length keeps 0.4; it is limited to 0.1 - 2 s like the reference's live ZOOM["len"].</summary>
    public static EffectSettings Effects(AppState s, IEnumerable<Zoom> zoomPlay)
    {
        string mode = Text.InterpKey(s.SlowInterp);
        double len = ParseFloat(s.ZoomLen.Replace(",", ".")) is { } v && !double.IsNaN(v) ? Max(0.1, Min(2.0, v)) : 0.4;
        return new EffectSettings
        {
            Fast = Text.AudKey(s.FastAudio), Slow = Text.AudKey(s.SlowAudio),
            Deep = s.SlowPitch == C.SlowPitch[0], High = s.FastPitch == C.FastPitch[0],
            FastVol = s.FastVol, SlowVol = s.SlowVol, VhsOn = s.Vhs,
            SlowBlend = mode != "repeat", SlowRife = mode == "rife",
            ZoomMotion = s.ZoomFx == C.ZoomFx[0], ZoomLen = len, ZoomSound = s.ZoomSnd == C.ZoomAudio[0], ZoomVol = s.ZoomVol,
            ZoomPlay = zoomPlay.ToList(),
            PictureVf = s.PictureFilter(),
            AudioAf = s.EqFilter(),
        };
    }
}
