using ClipShrinker.Core.Py;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Ffmpeg;

public sealed record FilterGraph(string Graph, string VideoLabel, IReadOnlyList<string>? AudioLabels)
{
    public IReadOnlyList<string> Parts => Graph.Split(';');
}

/// <summary>The ffmpeg filter_complex for one or more clips (reference build_graph, lines 1048-1152).</summary>
public static class GraphBuilder
{
    /// <summary>Number of audio tracks in the output file.</summary>
    public static int OutTracks(IEnumerable<ExportClip> clips, bool mix = false) => OutTracks(clips.Select(c => c.AudioTracks), mix);

    public static int OutTracks(IEnumerable<int> trackCounts, bool mix = false)
    {
        int m = trackCounts.Max();
        return m == 0 ? 0 : (mix ? 1 : Math.Min(C.MaxAudio, m));
    }

    public static FilterGraph Build(IReadOnlyList<ExportClip> clips, int W, int H, double? ratio, string? xfade, double T,
                                    double fps, EffectSettings fx, bool tonemap = false, bool muteFast = true, bool mix = false,
                                    IReadOnlyDictionary<int, IReadOnlyDictionary<int, RifeInput>>? rife = null)
    {
        int n = clips.Count;
        int nOut = OutTracks(clips, mix);
        bool anyAudio = nOut > 0;
        var allPieces = clips.Select(c => c.GetPieces()).ToList();
        var durs = allPieces.Select(Timing.Duration).ToList();
        var parts = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var c = clips[i];
            var pieces = allPieces[i];
            bool seg = !Timing.IsPlain(pieces);
            double srcDur = c.End - c.Start;
            var f = Filters.ClipVfilters(c, W, H, ratio, tonemap);
            var tail = new List<string>();
            var (cw0, ch0) = Geometry.CropSize(c.W, c.H, ratio);
            var zs = Timing.ZoomSpans(c.Zooms, pieces, cw0, ch0, W, H);
            if (zs.Count > 0) tail.Add(Filters.ZoomVfilter(zs, W, H, fx));
            if (n > 1 || seg) tail.Add($"fps={PyFraction.Ratio(fps)}");
            tail.AddRange(["format=yuv420p", "settb=AVTB", "setpts=PTS-STARTPTS"]);
            string si = I(i);
            if (seg)
            {
                parts.Add($"[{si}:v:0]" + string.Join(",", f.Append("setpts=PTS-STARTPTS")) + $"[b{si}]");
                IReadOnlyDictionary<int, RifeInput>? r = rife is not null && rife.TryGetValue(i, out var ri) ? ri : null;
                parts.AddRange(Filters.SegVideo($"b{si}", pieces, c.Start, $"c{si}", $"m{si}", (W, H), fps, r, fx));
                parts.Add($"[m{si}]" + string.Join(",", tail) + $"[v{si}]");
            }
            else parts.Add($"[{si}:v:0]" + string.Join(",", f.Concat(tail)) + $"[v{si}]");

            if (!anyAudio) continue;
            int na = c.AudioTracks;
            var streams = new List<string>();
            for (int k = 0; k < na; k++)
            {
                string lbl = $"ab{si}_{I(k)}";
                parts.Add($"[{si}:a:{I(k)}]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo," +
                          $"apad=whole_dur={F(srcDur, 3)},atrim=duration={F(srcDur, 3)}," +
                          $"asetpts=PTS-STARTPTS[{lbl}]");
                streams.Add(lbl);
            }
            if (mix && streams.Count > 1)
            {
                parts.Add(string.Concat(streams.Select(x => $"[{x}]")) + C.MixFilter(streams.Count) + $"[abm{si}]");
                streams = [$"abm{si}"];
            }
            var zt = fx.ZoomSound ? Timing.ZoomTimes(c.Zooms, pieces) : [];
            for (int t = 0; t < nOut; t++)
            {
                if (t < streams.Count)
                {
                    bool whoosh = zt.Count > 0 && t == 0;
                    string lab = whoosh ? $"a{si}_{I(t)}z" : $"a{si}_{I(t)}";
                    if (seg) parts.AddRange(Filters.SegAudio(streams[t], pieces, c.Start, $"d{si}_{I(t)}", lab, muteFast, fx));
                    else parts.Add($"[{streams[t]}]anull[{lab}]");
                    if (whoosh)
                        parts.AddRange(Filters.ZoomWhooshParts(lab, $"a{si}_{I(t)}", zt.Select(z => (z.OutStart, z.OutEnd)).ToList(),
                                                               48000, $"zw{si}", fx));
                }
                else
                {
                    parts.Add($"anullsrc=r=48000:cl=stereo,atrim=duration={F(durs[i], 3)}," +
                              $"asetpts=PTS-STARTPTS[a{si}_{I(t)}]");
                }
            }
        }
        List<string>? Labels(string prefix) => anyAudio ? Enumerable.Range(0, nOut).Select(t => prefix + I(t)).ToList() : null;
        if (n == 1) return new FilterGraph(string.Join(";", parts), "v0", Labels("a0_"));
        if (string.IsNullOrEmpty(xfade))
        {
            string ins = string.Concat(Enumerable.Range(0, n).Select(i =>
                $"[v{I(i)}]" + string.Concat(Enumerable.Range(0, nOut).Select(t => $"[a{I(i)}_{I(t)}]"))));
            string outs = "[vout]" + string.Concat(Enumerable.Range(0, nOut).Select(t => $"[aout{I(t)}]"));
            parts.Add($"{ins}concat=n={I(n)}:v=1:a={I(nOut)}{outs}");
            return new FilterGraph(string.Join(";", parts), "vout", Labels("aout"));
        }
        double cum = durs[0];
        string prev = "v0";
        for (int i = 1; i < n; i++)
        {
            string o = $"vx{I(i)}";
            if (xfade == C.Glitch)
            {
                parts.Add($"[{prev}][v{I(i)}]xfade=transition=fadefast:duration={F(T, 3)}:offset={F(cum - T, 3)}[{o}g]");
                parts.AddRange(Filters.GlitchParts($"{o}g", o, cum - T, T, W, H, $"gl{I(i)}", seed: i * 31 + 7));
            }
            else parts.Add($"[{prev}][v{I(i)}]xfade=transition={xfade}:duration={F(T, 3)}:offset={F(cum - T, 3)}[{o}]");
            prev = o;
            cum = cum + durs[i] - T;
        }
        string vlast = prev;
        List<string>? alast = null;
        if (anyAudio)
        {
            alast = [];
            for (int t = 0; t < nOut; t++)
            {
                string p = $"a0_{I(t)}";
                for (int i = 1; i < n; i++)
                {
                    string o = $"ax{I(i)}_{I(t)}";
                    parts.Add($"[{p}][a{I(i)}_{I(t)}]acrossfade=d={F(T, 3)}:c1=tri:c2=tri[{o}]");
                    p = o;
                }
                alast.Add(p);
            }
        }
        return new FilterGraph(string.Join(";", parts), vlast, alast);
    }
}
