using ClipShrinker.Core.Py;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Ffmpeg;

/// <summary>A slow-motion piece rendered by RIFE: extra ffmpeg input index and the rendered frame size.</summary>
public readonly record struct RifeInput(int InputIndex, int W, int H);

/// <summary>ffmpeg filter-graph pieces (reference lines 242-268, 611-618, 721-1069).</summary>
public static class Filters
{
    static string F3(double x) => F(x, 3);

    public static IReadOnlyList<string> VideoCodecArgs(string codec, double vBps)
    {
        long kb = Math.Max(20, Int(vBps / 1000));
        var a = new List<string> { "-c:v", codec, "-preset", "p5", "-tune", "hq", "-rc", "vbr",
            "-b:v", $"{I(kb)}k", "-maxrate", $"{I(kb)}k", "-bufsize", $"{I(kb * 2)}k",
            "-spatial-aq", "1", "-pix_fmt", "yuv420p" };
        if (codec == "hevc_nvenc") a.AddRange(["-tag:v", "hvc1"]);
        return a;
    }

    public static IReadOnlyList<string> AtempoChain(double sp)
    {
        var f = new List<string>();
        while (sp < 0.5) { f.Add("atempo=0.5"); sp /= 0.5; }
        while (sp > 2.0) { f.Add("atempo=2.0"); sp /= 2.0; }
        f.Add($"atempo={F(sp, 5)}");
        return f;
    }

    /// <summary>Digital "glitch" over [a, a+T] of [src] -> [out].</summary>
    public static List<string> GlitchParts(string src, string @out, double a, double T, int W, int H, string tag, long seed = 7)
    {
        var rnd = new PyRandom(seed);
        double b = a + T, mid = a + T / 2;
        static string En(double x, double y) => $"enable='between(t,{F3(x)},{F3(y)})'";
        long s1 = Math.Max(2, FloorDiv(W, 110));
        var chain = new[]
        {
            $"rgbashift=rh={I(-s1)}:bh={I(s1)}:edge=smear:{En(a, b)}",
            $"rgbashift=rh={I(2 * s1)}:gv={I(-s1)}:bh={I(-2 * s1)}:edge=smear:{En(mid - T * 0.18, mid + T * 0.18)}",
            $"noise=alls=28:allf=t:{En(a + T * 0.15, b - T * 0.15)}",
            $"lutyuv=y='val*1.35':{En(mid - T * 0.06, mid + T * 0.04)}",
        };
        const int nb = 6;
        var parts = new List<string>
        {
            $"[{src}]" + string.Join(",", chain) + $",split=2[{tag}m0][{tag}s]",
            $"[{tag}s]split={I(nb)}" + string.Concat(Enumerable.Range(0, nb).Select(k => $"[{tag}s{I(k)}]")),
        };
        string cur = $"{tag}m0";
        int[] signs = [-1, 1];
        for (int k = 0; k < nb; k++)
        {
            long h = Math.Max(2, FloorDiv(Int(H * rnd.Uniform(0.03, 0.14)), 2) * 2);
            long y = FloorDiv(Int(rnd.Uniform(0, H - h)), 2) * 2;
            long dx = Int(W * rnd.Uniform(0.04, 0.22)) * rnd.Choice(signs);
            double t1 = a + T * rnd.Uniform(0.05, 0.75);
            double t2 = Min(b, t1 + T * rnd.Uniform(0.12, 0.3));
            string nxt = k == nb - 1 ? @out : $"{tag}m{I(k + 1)}";
            parts.Add($"[{tag}s{I(k)}]crop={I(W)}:{I(h)}:0:{I(y)}[{tag}c{I(k)}]");
            parts.Add($"[{cur}][{tag}c{I(k)}]overlay=x={I(dx)}:y={I(y)}:{En(t1, t2)}[{nxt}]");
            cur = nxt;
        }
        return parts;
    }

    /// <summary>Stream [i] (one sped-up section, D seconds) -> [o] with a VHS look.</summary>
    public static List<string> VhsVideoParts(string i, string o, double D, (int W, int H)? size)
    {
        double B = Min(0.35, D / 2);
        string en = $"enable='lt(t,{F3(B)})+gt(t,{F3(D - B)})'";
        var chain = string.Join(",", "noise=alls=10:allf=t", "rgbashift=rh=-2:bh=2", "hue=s=1.2",
            $"noise=alls=60:allf=t+u:{en}", $"rgbashift=rh=-9:bh=9:{en}", $"hue=s=0.5:{en}", $"lutyuv=y='val+16':{en}");
        if (size is null) return [$"[{i}]" + chain + $",format=yuv420p[{o}]"];
        var (W, H) = size.Value;
        long bh = Math.Max(8, Int(H * 0.05));
        return
        [
            $"[{i}]" + chain + $"[{o}_f]",
            $"color=c=white:s={I(W)}x{I(bh)}:r=30:d={F3(D + 0.5)},format=rgba,colorchannelmixer=aa=0.16[{o}_k]",
            $"[{o}_f][{o}_k]overlay=x=0:y='mod(t*{F(H * 0.45, 0)},{I(H + bh)})-{I(bh)}':eof_action=pass:format=yuv420[{o}]",
        ];
    }

    /// <summary>Cuts stream [src] into pieces, changes their speed and joins them again.</summary>
    public static List<string> SegVideo(string src, IReadOnlyList<Piece> pieces, double tBase, string tag, string @out,
                                        (int W, int H)? size, double? fps, IReadOnlyDictionary<int, RifeInput>? rife,
                                        EffectSettings fx)
    {
        List<string> One(Piece p, string i, string o, int j)
        {
            var (a, b, sp) = p;
            if (rife is { Count: > 0 } && rife.TryGetValue(j, out var r))
            {
                string sc = size is { } s && (r.W, r.H) != s ? $",scale={I(s.W)}:{I(s.H)}:flags=lanczos,setsar=1" : "";
                return [$"[{i}]nullsink", $"[{I(r.InputIndex)}:v:0]setpts=PTS-STARTPTS{sc},format=yuv420p[{o}]"];
            }
            string bas = $"[{i}]trim=start={F(a - tBase, 4)}:end={F(b - tBase, 4)},setpts=(PTS-STARTPTS)/{F(sp, 5)}";
            double D = (b - a) / sp;
            if (sp < 1.0 && fx.SlowBlend && fps is { } f && f != 0)
                bas += $",framerate=fps={PyFraction.Ratio(f)}:interp_start=0:interp_end=255:scene=100";
            if (fx.VhsOn && sp > 1.0 && D >= C.VhsMin)
                return [bas + $"[{o}_r]", .. VhsVideoParts($"{o}_r", o, D, size)];
            return [bas + $"[{o}]"];
        }
        int k = pieces.Count;
        if (k == 1) return One(pieces[0], src, @out, 0);
        var parts = new List<string> { $"[{src}]split={I(k)}" + string.Concat(Enumerable.Range(0, k).Select(j => $"[{tag}s{I(j)}]")) };
        for (int j = 0; j < k; j++) parts.AddRange(One(pieces[j], $"{tag}s{I(j)}", $"{tag}p{I(j)}", j));
        parts.Add(string.Concat(Enumerable.Range(0, k).Select(j => $"[{tag}p{I(j)}]")) + $"concat=n={I(k)}:v=1:a=0[{@out}]");
        return parts;
    }

    /// <summary>Section audio [m] + a tape-wind burst at its start (rising) and end (falling).</summary>
    public static List<string> VhsAudioParts(string m, string o, double D, int rate)
    {
        double L = Min(0.45, D / 2);
        double fo = Max(0.0, L - 0.12);
        long ms = Int((D - L) * 1000);
        var parts = new List<string>();
        var items = new[] { ($"220*t+2600*t*t/{F3(L)}", ""), ($"2800*t-1300*t*t/{F3(L)}", $",adelay={I(ms)}|{I(ms)}") };
        for (int n = 0; n < items.Length; n++)
        {
            var (ph, dl) = items[n];
            string expr = $"0.214*sin(2*PI*({ph}))";
            parts.Add($"anoisesrc=d={F3(L)}:c=pink:r={I(rate)}:a=0.34,highpass=f=250,lowpass=f=7000," +
                      $"aformat=sample_fmts=fltp:channel_layouts=stereo[{o}_n{I(n)}]");
            parts.Add($"aevalsrc='{expr}|{expr}':s={I(rate)}:d={F3(L)},aformat=sample_fmts=fltp:channel_layouts=stereo" +
                      $"[{o}_t{I(n)}]");
            parts.Add($"[{o}_n{I(n)}][{o}_t{I(n)}]amix=inputs=2:normalize=0,afade=t=in:d=0.03," +
                      $"afade=t=out:st={F3(fo)}:d=0.12{dl}[{o}_b{I(n)}]");
        }
        parts.Add($"[{m}][{o}_b0][{o}_b1]amix=inputs=3:duration=first:normalize=0[{o}]");
        return parts;
    }

    /// <summary>Section audio [m] + a deep "bullet time" thump / rumble at the start and a swell at the end.</summary>
    public static List<string> SlowAudioParts(string m, string o, double D, int rate)
    {
        double L = Min(0.6, D / 2);
        long ms = Int((D - L) * 1000);
        string ph0 = $"260*t-215*t*t/(2*{F3(L)})";
        string ph1 = $"45*t+215*t*t/(2*{F3(L)})";
        string e0 = $"0.12*sin(2*PI*({ph0}))*exp(-3*t/{F3(L)})+0.18*sin(2*PI*48*t)*exp(-9*t)";
        string e1 = $"0.13*sin(2*PI*({ph1}))*sin(PI*t/{F3(L)})";
        var items = new[]
        {
            (e0, "", $"lowpass=f=500,afade=t=out:d={F3(L)}"),
            (e1, $",adelay={I(ms)}|{I(ms)}", $"lowpass=f=700,afade=t=in:d={F3(L)}"),
        };
        var parts = new List<string>();
        for (int n = 0; n < items.Length; n++)
        {
            var (expr, dl, nz) = items[n];
            parts.Add($"aevalsrc='{expr}|{expr}':s={I(rate)}:d={F3(L)},aformat=sample_fmts=fltp:channel_layouts=stereo[{o}_t{I(n)}]");
            parts.Add($"anoisesrc=d={F3(L)}:c=brown:r={I(rate)}:a=0.09,{nz},aformat=sample_fmts=fltp:channel_layouts=stereo[{o}_n{I(n)}]");
            parts.Add($"[{o}_t{I(n)}][{o}_n{I(n)}]amix=inputs=2:normalize=0,afade=t=in:d=0.01,afade=t=out:st={F3(Max(0.0, L - 0.08))}:d=0.08{dl}[{o}_b{I(n)}]");
        }
        parts.Add($"[{m}][{o}_b0][{o}_b1]amix=inputs=3:duration=first:normalize=0[{o}]");
        return parts;
    }

    /// <summary>Cuts audio [src] into pieces, changes their speed / pitch / volume and joins them again.</summary>
    public static List<string> SegAudio(string src, IReadOnlyList<Piece> pieces, double tBase, string tag, string @out,
                                        bool muteFast, EffectSettings fx, int rate = 48000)
    {
        List<string> One(Piece p, string i, string o)
        {
            var (a, b, sp) = p;
            var f = new List<string> { $"atrim=start={F(a - tBase, 4)}:end={F(b - tBase, 4)}", "asetpts=PTS-STARTPTS" };
            bool changed = Math.Abs(sp - 1.0) > 1e-6;
            if (changed)
            {
                string mode = sp > 1.0 ? fx.Fast : fx.Slow;
                if (sp < 1.0 && fx.Deep && mode != "mute")
                    f.AddRange([$"asetrate={I(Math.Max(1000, Round(rate * sp)))}", $"aresample={I(rate)}"]);
                else if (sp > 1.0 && fx.High && mode != "mute")
                    f.AddRange([$"asetrate={I(Round(rate * sp))}", $"aresample={I(rate)}"]);
                else
                    f.AddRange(AtempoChain(sp));
                if (mode == "mute") f.Add("volume=0");
            }
            double D = (b - a) / sp;
            double vol = 1.0;
            if (changed) vol = Math.Max(0, Math.Min(200, sp > 1.0 ? fx.FastVol : fx.SlowVol)) / 100.0;
            string? vf = Math.Abs(vol - 1.0) > 1e-6 ? $"volume={F(vol, 2)}" : null;
            if (changed && D >= C.VhsMin)
            {
                string mode = sp > 1.0 ? fx.Fast : fx.Slow;
                if (mode == "fx")
                {
                    Func<string, string, double, int, List<string>> fn = sp > 1.0 ? VhsAudioParts : SlowAudioParts;
                    if (vf is not null)
                        return [$"[{i}]" + string.Join(",", f) + $"[{o}_m]", .. fn($"{o}_m", $"{o}_x", D, rate), $"[{o}_x]{vf}[{o}]"];
                    return [$"[{i}]" + string.Join(",", f) + $"[{o}_m]", .. fn($"{o}_m", o, D, rate)];
                }
            }
            if (vf is not null) f.Add(vf);
            return [$"[{i}]" + string.Join(",", f) + $"[{o}]"];
        }
        int k = pieces.Count;
        if (k == 1) return One(pieces[0], src, @out);
        var parts = new List<string> { $"[{src}]asplit={I(k)}" + string.Concat(Enumerable.Range(0, k).Select(j => $"[{tag}a{I(j)}]")) };
        for (int j = 0; j < k; j++) parts.AddRange(One(pieces[j], $"{tag}a{I(j)}", $"{tag}q{I(j)}"));
        parts.Add(string.Concat(Enumerable.Range(0, k).Select(j => $"[{tag}q{I(j)}]")) + $"concat=n={I(k)}:v=0:a=1[{@out}]");
        return parts;
    }

    /// <summary>Video filters that zoom into the windows (smooth ease in / out when the motion effect is on).</summary>
    public static string ZoomVfilter(IReadOnlyList<ZoomSpan> spans, int W, int H, EffectSettings fx)
    {
        bool motion = fx.ZoomMotion;
        double T = Max(0.1, fx.ZoomLen);
        var lf = new List<string>();
        var tp = new List<string>();
        var wd = new List<string> { "1" };
        foreach (var (oa, ob, l, t, w) in spans)
        {
            string e;
            if (motion)
            {
                double Tm = Max(0.05, Min(T, (ob - oa) / 2));
                string k = $"clip(min(t-{F3(oa)},{F3(ob)}-t)/{F3(Tm)},0,1)";
                e = $"({k}*{k}*(3-2*{k}))";
            }
            else e = $"between(t,{F3(oa)},{F3(ob)})";
            lf.Add($"{e}*{F(l, 5)}");
            tp.Add($"{e}*{F(t, 5)}");
            wd.Add($"{e}*({F(w - 1, 5)})");
        }
        string WD = "(" + string.Join("+", wd) + ")", LEFT = "(" + string.Join("+", lf) + ")", TOP = "(" + string.Join("+", tp) + ")";
        string Ws = I(W), Hs = I(H);
        return $"setpts=PTS-STARTPTS,scale=w='trunc({Ws}/{WD}/2)*2':h='trunc({Hs}/{WD}/2)*2':eval=frame," +
               $"crop={Ws}:{Hs}:x='max(0,min({LEFT}*{Ws}/{WD},{Ws}/{WD}-{Ws}-2))':" +
               $"y='max(0,min({TOP}*{Hs}/{WD},{Hs}/{WD}-{Hs}-2))'";
    }

    /// <summary>[a_in] + a short whoosh when each zoom starts (rising) and ends (falling) -> [a_out].</summary>
    public static List<string> ZoomWhooshParts(string aIn, string aOut, IReadOnlyList<(double OutStart, double OutEnd)> times,
                                               int rate, string tag, EffectSettings fx)
    {
        double vol = Math.Max(0, Math.Min(200, fx.ZoomVol)) / 100.0;
        if (times.Count == 0 || vol <= 0) return [$"[{aIn}]anull[{aOut}]"];
        var parts = new List<string>();
        var labs = new List<string>();
        int n = 0;
        foreach (var (oa, ob) in times)
        {
            double L = Min(!fx.ZoomMotion ? 0.6 : Max(0.3, Min(fx.ZoomLen, 0.8)), (ob - oa) / 2);
            foreach (var (pos, f0, f1) in new[] { (oa, 250, 1500), (ob - L, 1500, 250) })
            {
                string expr = $"(0.13*(random(0)*2-1)+0.065*sin(2*PI*({I(f0)}*t+({I(f1)}-{I(f0)})*t*t/(2*{F3(L)}))))" +
                              $"*pow(sin(PI*t/{F3(L)}),2)";
                long ms = Math.Max(0, Int(pos * 1000));
                parts.Add($"aevalsrc='{expr}|{expr}':s={I(rate)}:d={F3(L)},highpass=f=200,lowpass=f=5500," +
                          $"aformat=sample_fmts=fltp:channel_layouts=stereo,volume={F(vol, 2)}," +
                          $"adelay={I(ms)}|{I(ms)}[{tag}w{I(n)}]");
                labs.Add($"[{tag}w{I(n)}]");
                n++;
            }
        }
        parts.Add($"[{aIn}]" + string.Concat(labs) + $"amix=inputs={I(n + 1)}:duration=first:normalize=0[{aOut}]");
        return parts;
    }

    /// <summary>Per-clip video filters before the speed sections: crop, scale + pad to W x H, HDR -> SDR.</summary>
    public static List<string> ClipVfilters(int w, int h, string? trc, int W, int H, double? ratio, bool tonemap)
    {
        var f = new List<string>();
        var (cw, ch) = Geometry.CropSize(w, h, ratio);
        if ((cw, ch) != (w, h)) f.Add($"crop={I(cw)}:{I(ch)}");
        f.Add($"scale={I(W)}:{I(H)}:force_original_aspect_ratio=decrease:flags=lanczos");
        f.Add($"pad={I(W)}:{I(H)}:(ow-iw)/2:(oh-ih)/2");
        f.Add("setsar=1");
        if (tonemap && !string.IsNullOrEmpty(trc)) f.Add(Geometry.TonemapChain(trc));
        return f;
    }

    public static List<string> ClipVfilters(ExportClip c, int W, int H, double? ratio, bool tonemap) =>
        ClipVfilters(c.W, c.H, c.Trc, W, H, ratio, tonemap);
}
