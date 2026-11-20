using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core;

/// <summary>Speed pieces and zoom timing (reference lines 631-718).</summary>
public static class Timing
{
    /// <summary>Splits [start, end] into pieces that follow the speed sections (1x elsewhere). 2.9: a pause (a point
    /// at A, B - A seconds long) adds a hold piece at A, inside a speed section too; the clip then goes on from A.</summary>
    public static IReadOnlyList<Piece> BuildPieces(double start, double end, IEnumerable<SpeedSection> sections)
    {
        var all = sections.ToList();
        var pieces = SpeedPieces(start, end, all.Where(x => !x.IsPause));
        foreach (var p in all.Where(x => x.IsPause && start - 1e-6 <= x.A && x.A < end && x.B - x.A >= 0.02).OrderBy(x => x.A))
        {
            double t = Max(p.A, start);
            int i = pieces.FindIndex(q => !q.IsHold && q.B > t);
            if (i < 0) continue;
            var q = pieces[i];
            if (t > q.A + 0.001)                             // inside a piece: split it there
            {
                pieces[i] = q with { B = t };
                pieces.Insert(i + 1, q with { A = t });
                i++;
            }
            pieces.Insert(i, new Piece(t, t, 1.0, p.B - p.A));
        }
        return pieces;
    }

    static List<Piece> SpeedPieces(double start, double end, IEnumerable<SpeedSection> sections)
    {
        var secs = sections.Where(x => x.B > start && x.A < end)
                           .Select(x => (a: Max(x.A, start), b: Min(x.B, end), sp: x.Speed))
                           .OrderBy(x => x.a).ThenBy(x => x.b).ThenBy(x => x.sp).ToList();
        var pieces = new List<Piece>();
        double pos = start;
        foreach (var (a0, b, sp) in secs)
        {
            double a = Max(a0, pos);
            if (b - a < 0.02) continue;
            if (a - pos > 0.001) pieces.Add(new Piece(pos, a, 1.0));
            pieces.Add(new Piece(a, b, sp));
            pos = b;
        }
        if (end - pos > 0.001) pieces.Add(new Piece(pos, end, 1.0));
        return pieces.Count > 0 ? pieces : [new Piece(start, end, 1.0)];
    }

    public static double Duration(IEnumerable<Piece> pieces) => Sum(pieces.Select(p => p.OutLen));

    public static bool IsPlain(IReadOnlyList<Piece>? pieces) =>
        pieces is null || pieces.Count == 0 || (pieces.Count == 1 && Math.Abs(pieces[0].Speed - 1.0) < 1e-6 && !pieces[0].IsHold);

    /// <summary>Time in the edited output -> time in the source clip.</summary>
    public static double OutToSrc(IReadOnlyList<Piece> pieces, double tOut)
    {
        double acc = 0.0;
        foreach (var p in pieces)
        {
            double d = p.OutLen;
            if (tOut < acc + d) return p.IsHold ? p.A : p.A + (tOut - acc) * p.Speed;   // (a pause shows its one frame)
            acc += d;
        }
        return pieces[^1].B;
    }

    /// <summary>Time in the source clip -> time in the edited output.</summary>
    public static double SrcToOut(IReadOnlyList<Piece> pieces, double t)
    {
        double acc = 0.0;
        foreach (var p in pieces)
        {
            if (t <= p.A) return acc;                        // (a time at a pause = the start of the pause)
            if (t < p.B) return acc + (t - p.A) / p.Speed;
            acc += p.OutLen;
        }
        return acc;
    }

    /// <summary>2.9: SrcToOut that also works outside the pieces (1x before and after them, as the export treats the
    /// trimmed-away parts): before the first piece the result is negative.</summary>
    public static double SrcToOutX(IReadOnlyList<Piece> pieces, double t) =>
        t < pieces[0].A ? t - pieces[0].A : t > pieces[^1].B ? Duration(pieces) + (t - pieces[^1].B) : SrcToOut(pieces, t);

    /// <summary>2.9: OutToSrc that also works outside the pieces (see SrcToOutX).</summary>
    public static double OutToSrcX(IReadOnlyList<Piece> pieces, double tOut)
    {
        double tot = Duration(pieces);
        return tOut < 0 ? pieces[0].A + tOut : tOut >= tot ? pieces[^1].B + (tOut - tot) : OutToSrc(pieces, tOut);
    }

    /// <summary>[(out_start, out_end, zoom)] of the zoom sections.</summary>
    public static IReadOnlyList<(double OutStart, double OutEnd, Zoom Z)> ZoomTimes(IReadOnlyList<Zoom>? zooms,
                                                                                  IReadOnlyList<Piece>? pieces)
    {
        var res = new List<(double, double, Zoom)>();
        if (zooms is null || zooms.Count == 0 || pieces is null || pieces.Count == 0) return res;
        double st = pieces[0].A, en = pieces[^1].B;
        foreach (var z in zooms)
        {
            double a = Max(z.A, st), b = Min(z.B, en);
            if (b - a < 0.05) continue;
            double oa = SrcToOut(pieces, a), ob = SrcToOut(pieces, b);
            if (ob - oa >= 0.05) res.Add((oa, ob, z));
        }
        return res;
    }

    /// <summary>Zoom windows in the output frame. (cw0, ch0) = size of the picture the area was drawn on.</summary>
    public static IReadOnlyList<ZoomSpan> ZoomSpans(IReadOnlyList<Zoom>? zooms, IReadOnlyList<Piece> pieces,
                                                    double cw0, double ch0, double W, double H)
    {
        double f = Min(W / cw0, H / ch0);
        double cw = cw0 * f, ch = ch0 * f;
        double ox = (W - cw) / 2, oy = (H - ch) / 2;
        var res = new List<ZoomSpan>();
        foreach (var (oa, ob, z) in ZoomTimes(zooms, pieces))
        {
            double wd = Min(1.0, Max(C.ZoomMinW * 0.5, z.W * cw / W));
            double cx = (ox + (z.X + z.W / 2) * cw) / W;
            double cy = (oy + (z.Y + z.W / 2) * ch) / H;
            res.Add(new ZoomSpan(oa, ob, Min(Max(cx - wd / 2, 0.0), 1 - wd), Min(Max(cy - wd / 2, 0.0), 1 - wd), wd));
        }
        return res;
    }
}

/// <summary>Picture-size helpers (reference lines 387-451).</summary>
public static class Geometry
{
    public static double ParseFps(string? s)
    {
        if (s is not null)
        {
            var ab = s.Split('/');
            if (ab.Length == 2)
            {
                var a = ParseFloat(ab[0]);
                var b = ParseFloat(ab[1]);
                if (a is not null && b is not null && b.Value != 0) return a.Value / b.Value;
            }
            var v = ParseFloat(s);
            if (v is not null) return v.Value;
        }
        return 30.0;
    }

    /// <summary>Round down to an even number (at least 2).</summary>
    public static int Even(double n) => (int)Math.Max(2, FloorDiv(Int(n), 2) * 2);

    /// <summary>Centered crop to ratio; only clips wider than the ratio are cut.</summary>
    public static (int W, int H) CropSize(int w, int h, double? ratio)
    {
        if (ratio is null || ratio.Value == 0 || (double)w / h <= ratio.Value + 0.005) return (w, h);
        return (Math.Min(w, Even(h * ratio.Value)), h);
    }

    /// <summary>mode: Auto, Original, 100%, 75%, 50%, or a fixed height (4K, 1440p, 1080p, 720p: the picture is made that
    /// high, never higher than it is). Returns the scale factor.</summary>
    public static double ChooseScale(string mode, double w, double h, double fps, double vBps)
    {
        if (mode == "Original") return 1.0;
        if (TargetHeight(mode) is { } th) return h > 0 ? Math.Min(1.0, th / h) : 1.0;
        if (mode != "Auto") return int.Parse(mode.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture) / 100.0;
        foreach (double s in new[] { 1.0, 0.75, 0.5 })
        {
            double bpp = vBps / (w * s * h * s * fps);
            if (bpp >= 0.05) return s;
        }
        return 0.5;
    }

    /// <summary>2.6: the output height of a fixed resolution option ("4K" = 2160, "1080p" = 1080), else null.</summary>
    public static int? TargetHeight(string mode) =>
        mode == "4K" ? 2160 : mode.EndsWith('p') && int.TryParse(mode[..^1], System.Globalization.CultureInfo.InvariantCulture, out int px) ? px : null;

    /// <summary>HDR (PQ or HLG, BT.2020) -> SDR BT.709, 8-bit (zscale).</summary>
    public static string TonemapChain(string trc) =>
        $"zscale=tin={trc}:pin=2020:min=2020_ncl:t=linear:p=2020:npl=203,format=gbrpf32le," +
        "zscale=p=709,tonemap=tonemap=mobius:param=0.8:desat=0," +
        "zscale=t=709:m=709:r=tv,format=yuv420p";
}
