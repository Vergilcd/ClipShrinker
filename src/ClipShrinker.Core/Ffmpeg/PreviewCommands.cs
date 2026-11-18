using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Ffmpeg;

/// <summary>ffmpeg command lines for previews, thumbnails and sound samples (reference lines 454-587, 1167-1259, 5752-5777).</summary>
public static class PreviewCommands
{
    /// <summary>Executable name or path used as argv[0]. The app sets this to library\ffmpeg.exe.</summary>
    public static string Ffmpeg { get; set; } = "ffmpeg";

    /// <summary>One frame as PNG on stdout, optionally center-cropped and tone-mapped.</summary>
    public static List<string> GrabFrame(string path, double t, int w, int h, (int W, int H)? crop = null, string? trc = null,
                                         string? picture = null)
    {
        string vf = (crop is { } c ? $"crop={I(c.W)}:{I(c.H)}," : "") +
                    $"scale={I(w)}:{I(h)}:force_original_aspect_ratio=decrease" + (trc is not null ? "," + Geometry.TonemapChain(trc) : "")
                    + (picture is not null ? "," + picture : "");
        return [Ffmpeg, "-v", "error", .. Modules.PictureAdjust.HwArgs([picture]), "-ss", F(t, 3), "-i", path, "-frames:v", "1",
                "-vf", vf, "-f", "image2pipe", "-vcodec", "png", "-"];
    }

    /// <summary>One batch of thumbnails (at most ThumbBatch times) as a PNG stream from one ffmpeg process.</summary>
    public static List<string> GrabFramesBatch(string path, IReadOnlyList<double> batch, int w, int h, string? trc = null)
    {
        var cmd = new List<string> { Ffmpeg, "-v", "error", "-nostdin" };
        var parts = new List<string>();
        var labs = new List<string>();
        for (int i = 0; i < batch.Count; i++)
        {
            cmd.AddRange(["-noaccurate_seek", "-ss", F(batch[i], 3), "-t", "2", "-i", path]);
            string vf = $"trim=end_frame=1,setpts=PTS-STARTPTS,scale={I(w)}:{I(h)}:force_original_aspect_ratio=decrease";
            if (trc is not null) vf += "," + Geometry.TonemapChain(trc);
            parts.Add($"[{I(i)}:v:0]{vf},format=rgb24[v{I(i)}]");
            labs.Add($"[v{I(i)}]");
        }
        parts.Add(string.Concat(labs) + $"concat=n={I(batch.Count)}:v=1:a=0[o]");
        cmd.AddRange(["-filter_complex", string.Join(";", parts), "-map", "[o]", "-fps_mode", "passthrough",
                      "-f", "image2pipe", "-vcodec", "png", "-"]);
        return cmd;
    }

    /// <summary>Raw RGB frames for the player (it paces them itself). The input is cut at start.</summary>
    public static List<string> Play(string path, double start, double dur, (int W, int H)? crop, int w, int h, double fps,
                                    int hwdec, string? trc, IReadOnlyList<Piece>? pieces, EffectSettings fx)
    {
        var cmd = new List<string> { Ffmpeg, "-v", "error", "-nostdin" };
        IReadOnlyList<Piece> pcs = pieces is { Count: > 0 } ? pieces : [new Piece(start, start + dur, 1.0)];
        var zs = fx.ZoomPlay.Count > 0
            ? Timing.ZoomSpans(fx.ZoomPlay, pcs, crop?.W ?? w, crop?.H ?? h, w, h) : [];
        bool plain = Timing.IsPlain(pieces);
        bool gpu = hwdec == 2 && plain && crop is null && trc is null && zs.Count == 0 && fx.PictureVf is null;
        cmd.AddRange(Modules.PictureAdjust.HwArgs([fx.PictureVf]));
        if (gpu) cmd.AddRange(["-hwaccel", "cuda", "-hwaccel_output_format", "cuda"]);
        else if (hwdec != 0) cmd.AddRange(["-hwaccel", "cuda"]);
        cmd.AddRange(["-probesize", "5000000", "-analyzeduration", "1000000",
                      "-ss", F(start, 3), "-t", F(dur, 3), "-i", path, "-an"]);
        if (gpu)
            cmd.AddRange(["-vf", $"scale_cuda={I(w)}:{I(h)}:format=nv12,hwdownload,format=nv12,fps={G(fps)}"]);
        else if (plain && zs.Count == 0)
        {
            string vf = (crop is { } c ? $"crop={I(c.W)}:{I(c.H)}," : "") + $"fps={G(fps)},scale={I(w)}:{I(h)}:flags=fast_bilinear";
            if (trc is not null) vf += "," + Geometry.TonemapChain(trc);
            if (fx.PictureVf is { } pic) vf += "," + pic;
            cmd.AddRange(["-vf", vf]);
        }
        else
        {
            var bas = new List<string>();
            if (crop is { } c) bas.Add($"crop={I(c.W)}:{I(c.H)}");
            bas.Add($"scale={I(w)}:{I(h)}:flags=fast_bilinear");
            if (trc is not null) bas.Add(Geometry.TonemapChain(trc));
            if (fx.PictureVf is { } pic) bas.Add(pic);
            string zf = zs.Count > 0 ? Filters.ZoomVfilter(zs, w, h, fx) + "," : "";
            var graph = new List<string> { "[0:v:0]" + string.Join(",", bas.Append("setpts=PTS-STARTPTS")) + "[pb]" };
            graph.AddRange(Filters.SegVideo("pb", pcs, start, "q", "pm", (w, h), fps, null, fx));
            graph.Add($"[pm]{zf}fps={G(fps)}[pout]");
            cmd.AddRange(["-filter_complex", string.Join(";", graph), "-map", "[pout]"]);
        }
        cmd.AddRange(["-f", "rawvideo", "-pix_fmt", "rgb24", "-"]);
        return cmd;
    }

    /// <summary>Preview audio: a WAV file (output = path) or raw 44.1 kHz stereo s16le on stdout.</summary>
    public static List<string> PreviewAudio(string path, double start, double dur, IReadOnlyList<Piece>? pieces, bool muteFast,
                                            int naudio, string? output, EffectSettings fx)
    {
        var cmd = new List<string> { Ffmpeg, "-y", "-v", "error", "-nostdin", "-probesize", "5000000", "-analyzeduration", "1000000",
                                     "-ss", F(start, 3), "-t", F(dur, 3), "-i", path, "-vn" };
        IReadOnlyList<Piece> pcs = pieces is { Count: > 0 } ? pieces : [new Piece(start, start + dur, 1.0)];
        var zt = fx.ZoomPlay.Count > 0 && fx.ZoomSound ? Timing.ZoomTimes(fx.ZoomPlay, pcs) : [];
        var uses = fx.SoundPlay.Count > 0 ? Sounds.ToUses(fx.SoundPlay, start, start + dur, pcs) : [];
        bool silent = naudio <= 0 && uses.Count > 0;         // (2.7: a clip without sound can still have Sound track sounds)
        naudio = Math.Max(1, naudio);
        if (uses.Count > 0) cmd.InsertRange(cmd.IndexOf("-vn"), uses.SelectMany(u => new[] { "-i", u.Path }));
        bool plain = Timing.IsPlain(pieces);
        if (plain && naudio == 1 && zt.Count == 0 && uses.Count == 0 && !silent)
        {
            cmd.AddRange(["-map", "0:a:0"]);
            if (fx.AudioAf is { } af0) cmd.AddRange(["-af", af0]);           // (2.6: Sound equalizer)
        }
        else
        {
            var parts = new List<string>();
            var labs = new List<string>();
            for (int k = 0; k < naudio; k++)
            {
                parts.Add((silent ? "anullsrc=r=44100:cl=stereo," : $"[0:a:{I(k)}]aresample=44100,aformat=channel_layouts=stereo,") + $"apad=whole_dur={F(dur, 3)}," +
                          $"atrim=duration={F(dur, 3)},asetpts=PTS-STARTPTS[p{I(k)}]");
                labs.Add($"[p{I(k)}]");
            }
            if (naudio > 1) parts.Add(string.Concat(labs) + C.MixFilter(naudio) + "[ab]");
            else parts[^1] = parts[^1].Replace("[p0]", "[ab]");
            string fin = zt.Count > 0 ? "aoz" : "aout";
            if (plain) parts.Add($"[ab]anull[{fin}]");
            else parts.AddRange(Filters.SegAudio("ab", pieces!, start, "w", fin, muteFast, fx, 44100));
            if (zt.Count > 0)
                parts.AddRange(Filters.ZoomWhooshParts("aoz", "aout", zt.Select(z => (z.OutStart, z.OutEnd)).ToList(), 44100, "zp", fx));
            string last = "aout";
            if (uses.Count > 0) { parts.AddRange(Sounds.MixParts(last, "asn", uses, 1, 44100, "ps")); last = "asn"; }   // (2.7)
            if (fx.AudioAf is { } af) { parts.Add($"[{last}]{af}[aeq]"); last = "aeq"; }
            cmd.AddRange(["-filter_complex", string.Join(";", parts), "-map", $"[{last}]"]);
        }
        cmd.AddRange(["-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le"]);
        if (output is not null) cmd.Add(output);
        else cmd.AddRange(["-f", "s16le", "-"]);
        return cmd;
    }

    /// <summary>A short sample of a sound effect: kind = fast | slow | zoom; path null = synthetic melody.</summary>
    public static List<string> EffectSample(string kind, string? path, double t0, string output, EffectSettings fx, int rate = 44100)
    {
        Piece[] pieces = kind switch
        {
            "fast" => [new(t0, t0 + 1.2, 1.0), new(t0 + 1.2, t0 + 4.8, 3.0), new(t0 + 4.8, t0 + 5.8, 1.0)],
            "slow" => [new(t0, t0 + 1.2, 1.0), new(t0 + 1.2, t0 + 2.2, 0.5), new(t0 + 2.2, t0 + 3.2, 1.0)],
            _ => [new(t0, t0 + 3.4, 1.0)],
        };
        double srcDur = pieces[^1].B - t0;
        var cmd = new List<string> { Ffmpeg, "-y", "-v", "error", "-nostdin" };
        string bas;
        if (!string.IsNullOrEmpty(path))
        {
            cmd.AddRange(["-ss", F(t0, 3), "-t", F(srcDur, 3), "-i", path]);
            bas = $"[0:a:0]aresample={I(rate)},aformat=sample_fmts=fltp:channel_layouts=stereo," +
                  $"apad=whole_dur={F(srcDur, 3)},atrim=duration={F(srcDur, 3)},asetpts=PTS-STARTPTS[ab]";
        }
        else
        {
            const string note = "220*pow(2,mod(floor(t*4)*5,12)/12)";
            string expr = $"0.25*sin(2*PI*t*{note})*exp(-mod(t,0.25)*6)";
            bas = $"aevalsrc='{expr}|{expr}':s={I(rate)}:d={F(srcDur, 3)}," +
                  "aformat=sample_fmts=fltp:channel_layouts=stereo[ab]";
        }
        var parts = new List<string> { bas };
        if (kind == "zoom") parts.AddRange(Filters.ZoomWhooshParts("ab", "eo", [(0.8, 2.6)], rate, "ez", fx));
        else parts.AddRange(Filters.SegAudio("ab", pieces, t0, "e", "eo", true, fx, rate));
        cmd.AddRange(["-filter_complex", string.Join(";", parts), "-map", "[eo]", "-ac", "2", "-ar", I(rate),
                      "-c:a", "pcm_s16le", output]);
        return cmd;
    }

    /// <summary>~1.6 s of a transition at hover-preview size as raw RGB on stdout.
    /// sources: the two (path, start) inputs, or null for plain colours.</summary>
    public static List<string> TransitionPreview(string transitionName, IReadOnlyList<(string Path, double T)>? sources)
    {
        string? xf = C.TransitionMode(transitionName);
        int W = C.PvW, H = C.PvH, Fr = C.PvFps;
        string one = $"scale={I(W)}:{I(H)}:force_original_aspect_ratio=decrease,pad={I(W)}:{I(H)}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={I(Fr)},format=yuv420p";
        var cmd = new List<string> { Ffmpeg, "-hide_banner", "-loglevel", "error" };
        string ga;
        if (sources is { Count: > 0 })
        {
            foreach (var (p, t) in sources) cmd.AddRange(["-ss", F(t, 3), "-t", "1.3", "-i", p]);
            ga = $"[0:v:0]{one},trim=duration=1.2,setpts=PTS-STARTPTS[a];[1:v:0]{one},trim=duration=1.2,setpts=PTS-STARTPTS[b]";
        }
        else
            ga = $"color=c=0x2f6db5:s={I(W)}x{I(H)}:r={I(Fr)}:d=1.2,format=yuv420p[a];" +
                 $"color=c=0xe08a2c:s={I(W)}x{I(H)}:r={I(Fr)}:d=1.2,format=yuv420p[b]";
        string g;
        if (xf == C.Glitch)
            g = ga + ";[a][b]xfade=transition=fadefast:duration=0.8:offset=0.4[g0];" +
                string.Join(";", Filters.GlitchParts("g0", "g1", 0.4, 0.8, W, H, "pg")) + ";[g1]format=rgb24[o]";
        else if (!string.IsNullOrEmpty(xf))
            g = ga + $";[a][b]xfade=transition={xf}:duration=0.8:offset=0.4,format=rgb24[o]";
        else
            g = ga + ";[a]trim=duration=0.8[a2];[b]trim=duration=0.8,setpts=PTS-STARTPTS[b2];[a2][b2]concat=n=2:v=1:a=0,format=rgb24[o]";
        cmd.AddRange(["-filter_complex", g, "-map", "[o]", "-frames:v", I(Int(1.6 * Fr)), "-f", "rawvideo", "pipe:1"]);
        return cmd;
    }
}
