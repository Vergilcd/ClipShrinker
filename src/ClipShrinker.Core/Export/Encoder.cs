using ClipShrinker.Core.Ffmpeg;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Export;

/// <summary>Runs one ffmpeg export command. The real one (Phase 2) parses -progress output.</summary>
public interface IFfmpegRunner
{
    /// <returns>ok = exit code 0; log = the last lines of ffmpeg's output.</returns>
    (bool Ok, string Log) Run(IReadOnlyList<string> cmd, double duration, string label, double pbase, double pspan);
    /// <summary>Kills the running process (Cancel).</summary>
    void Kill();
}

/// <summary>The file operations the encoder needs, so tests can fake them.</summary>
public interface IExportFileSystem
{
    long GetSize(string path);
    /// <summary>Move a over b, replacing b.</summary>
    void Replace(string a, string b);
    /// <summary>Delete; throws IOException (or a subclass) when it cannot.</summary>
    void Remove(string path);
}

public sealed class RealFileSystem : IExportFileSystem
{
    public long GetSize(string path) => new FileInfo(path).Length;
    public void Replace(string a, string b) => File.Move(a, b, overwrite: true);
    public void Remove(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        File.Delete(path);
    }
}

/// <summary>An installed rife-ncnn-vulkan (reference rife_exe / rife_model / rife_ready).</summary>
public sealed record RifeInstall(string Dir, string Exe, string Model)
{
    /// <summary>The exe and a v4 model (v4.6 if it is there) in dir, or null.</summary>
    public static RifeInstall? Find(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return null;
        string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "rife-ncnn-vulkan.exe" : "rife-ncnn-vulkan");
        if (!File.Exists(exe)) return null;
        List<string> names;
        try
        {
            names = Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>()
                .Where(n => n.StartsWith("rife-v4") && File.Exists(Path.Combine(dir, n, "flownet.param")))
                .Order(StringComparer.Ordinal).ToList();
        }
        catch (IOException) { return null; }
        if (names.Count == 0) return null;
        return new RifeInstall(dir, exe, Path.Combine(dir, names.Contains("rife-v4.6") ? "rife-v4.6" : names[^1]));
    }
}

public sealed class ExportCancelledException : Exception
{
    public ExportCancelledException() : base("The export was cancelled.") { }
}

/// <summary>An export error with the same message the 1.0.8 app shows.</summary>
public sealed class ExportException(string message) : Exception(message);

/// <summary>Extra ffmpeg input with RIFE-rendered frames.</summary>
public sealed record RifeExtra(string Dir, int Frames, double Duration);

/// <summary>Runs ffmpeg jobs and hits a target file size (reference class Encoder, lines 1467-1756). No GUI code.</summary>
public sealed class Encoder(IFfmpegRunner runner, IExportFileSystem fs, Action<double>? progress = null,
                            Action<string>? status = null, RifeInstall? rife = null, IToolRunner? tools = null,
                            Action<string>? log = null)
{
    readonly Action<double> progressCb = progress ?? (_ => { });
    readonly Action<string> statusCb = status ?? (_ => { });
    readonly Action<string> logCb = log ?? (_ => { });
    Dictionary<int, Dictionary<int, RifeInput>> rifeMap = [];
    List<RifeExtra> extras = [];
    bool rifeDone;
    string? rifeTmp;
    volatile bool cancelled;

    public bool UseHwDec { get; set; } = true;
    /// <summary>ffmpeg used for the export and the RIFE frame extraction.</summary>
    public string Ffmpeg { get; init; } = PreviewCommands.Ffmpeg;
    public bool Cancelled => cancelled;

    public void Cancel()
    {
        cancelled = true;
        runner.Kill();
        tools?.Kill();
    }

    IReadOnlyDictionary<int, IReadOnlyDictionary<int, RifeInput>> RifeMapView =>
        rifeMap.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, RifeInput>)kv.Value);

    /// <summary>Explicit output colour tags: SDR/BT.709 after tone-mapping, otherwise keep the HDR tags.</summary>
    public static List<string> ColorTags(IReadOnlyList<ExportClip> clips, bool tonemap)
    {
        var hdr = clips.Where(c => !string.IsNullOrEmpty(c.Trc)).ToList();
        if (hdr.Count == 0) return [];
        if (tonemap)
            return ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"];
        var c = hdr[0];
        var tags = new List<string> { "-color_trc", c.Trc! };
        if (!string.IsNullOrEmpty(c.Prim) && c.Prim != "unknown") tags.AddRange(["-color_primaries", c.Prim]);
        if (!string.IsNullOrEmpty(c.Space) && c.Space != "unknown") tags.AddRange(["-colorspace", c.Space]);
        return tags;
    }

    public List<string> Command(string output, IReadOnlyList<ExportClip> clips, FilterGraph graph, string codec, double vBps,
                                string audioK, bool hwdec, bool tonemap = false)
    {
        var cmd = new List<string> { Ffmpeg, "-y", "-hide_banner", "-nostats", "-progress", "pipe:1" };
        cmd.AddRange(Modules.PictureAdjust.HwArgs(clips.SelectMany(c => c.ModuleVf ?? [])));   // (GPU denoise)
        foreach (var c in clips)
        {
            if (hwdec) cmd.AddRange(["-hwaccel", "cuda"]);
            cmd.AddRange(["-ss", F(c.Start, 3), "-t", F(c.End - c.Start, 3), "-i", c.Path]);
        }
        foreach (var x in extras)
            cmd.AddRange(["-framerate", F(x.Frames / x.Duration, 6), "-start_number", "1", "-i", Path.Combine(x.Dir, "%08d.jpg")]);
        foreach (var s in clips.SelectMany(c => c.Sounds ?? []))   // 2.7: Sound track files, in the order the graph expects
            cmd.AddRange(["-i", s.Path]);
        cmd.AddRange(["-filter_complex", graph.Graph, "-map", $"[{graph.VideoLabel}]"]);
        foreach (var lbl in graph.AudioLabels ?? []) cmd.AddRange(["-map", $"[{lbl}]"]);
        cmd.AddRange(Filters.VideoCodecArgs(codec, vBps));
        cmd.AddRange(ColorTags(clips, tonemap));
        if (graph.AudioLabels is { Count: > 0 } al)
        {
            cmd.AddRange(["-c:a", "aac", "-b:a", audioK]);
            if (al.Count == 2)
                cmd.AddRange(["-metadata:s:a:0", "handler_name=Game audio", "-metadata:s:a:1", "handler_name=Microphone"]);
        }
        cmd.AddRange(["-movflags", "+faststart", output]);
        return cmd;
    }

    /// <summary>Exports clips to output. Returns the final file size in bytes.</summary>
    public long Encode(string output, IReadOnlyList<ExportClip> clips, ExportSettings st, double pbase = 0.0, double pspan = 100.0,
                       string tag = "")
    {
        rifeMap = [];
        extras = [];
        rifeDone = false;
        rifeTmp = null;
        try
        {
            return EncodeInner(output, clips, st, pbase, pspan, tag);
        }
        finally
        {
            if (rifeTmp is not null)
                try { Directory.Delete(rifeTmp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            rifeMap = [];
            extras = [];
            rifeTmp = null;
        }
    }

    static void DeleteDir(string d)
    {
        try { Directory.Delete(d, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Renders every slow-motion piece with RIFE into numbered JPEG frames (extra ffmpeg inputs).
    /// Returns the (pbase, pspan) left for the real encode. Any failure -> that piece keeps frame blending.</summary>
    (double pbase, double pspan) RifePrepare(IReadOnlyList<ExportClip> clips, int W, int H, double? ratio, bool tonemap,
                                             double pbase, double pspan)
    {
        rifeDone = true;
        var jobs = clips.SelectMany((c, i) => c.GetPieces().Select((p, j) => (i, j, p)))
                        .Where(x => x.p.Speed < 1.0 && (x.p.B - x.p.A) / x.p.Speed >= 0.2).ToList();
        if (jobs.Count == 0) return (pbase, pspan);
        if (rife is null || tools is null)
        {
            statusCb("RIFE is not installed - using frame blending for slow motion");
            return (pbase, pspan);
        }
        const double share = 0.35;
        rifeTmp = Directory.CreateTempSubdirectory("clipshrinker_rife_").FullName;
        for (int n = 0; n < jobs.Count; n++)
        {
            if (cancelled) throw new ExportCancelledException();
            var (i, j, (a, b, sp)) = jobs[n];
            var c = clips[i];
            double b0 = pbase + pspan * share * n / jobs.Count;
            double span = pspan * share / jobs.Count;
            string label = $"Slow motion (RIFE) {I(n + 1)}/{I(jobs.Count)}";
            statusCb(label + ": extracting frames");
            string dIn = Path.Combine(rifeTmp, $"i{I(n)}"), dOut = Path.Combine(rifeTmp, $"o{I(n)}");
            Directory.CreateDirectory(dIn);
            Directory.CreateDirectory(dOut);
            string vf = string.Join(",", Filters.ClipVfilters(c, W, H, ratio, tonemap).Append($"fps={Py.PyFraction.Ratio(c.Fps)}"));
            tools.Run([Ffmpeg, "-y", "-v", "error", "-nostdin", .. Modules.PictureAdjust.HwArgs([vf]), "-ss", F(a, 4), "-t", F(b - a, 4), "-i", c.Path,
                       "-an", "-vf", vf, "-q:v", "2", "-start_number", "1", Path.Combine(dIn, "%08d.jpg")]);
            if (cancelled) throw new ExportCancelledException();
            int cnt = Directory.GetFileSystemEntries(dIn).Length;
            if (cnt < 2)
            {
                logCb($"RIFE: no frames extracted for piece {a}-{b}");
                continue;
            }
            int nf = (int)Math.Max(cnt + 1, Round(cnt / sp));
            statusCb(label + ": 0%");
            int doneN = 0;
            var tail = new List<string>();
            int code = tools.Run([rife.Exe, "-i", dIn, "-o", dOut, "-n", I(nf), "-m", rife.Model, "-f", "%08d.jpg"], rife.Dir, line =>
            {
                if (line.Contains("done"))
                {
                    doneN++;
                    if (doneN % 4 == 0 || doneN == nf)
                    {
                        double frac = Math.Min(1.0, (double)doneN / nf);
                        progressCb(b0 + span * frac);
                        statusCb($"{label}: {F(frac * 100, 0)}%");
                    }
                }
                else
                {
                    tail.Add(line.Trim());
                    if (tail.Count > 8) tail.RemoveAt(0);
                }
            });
            if (cancelled) throw new ExportCancelledException();
            int got = Directory.GetFileSystemEntries(dOut).Length;
            if (code != 0 || got < nf)
            {
                logCb($"RIFE failed (code {code}, {got}/{nf} frames): " + string.Join(" | ", tail));
                statusCb(label + " failed - using frame blending for this section");
                DeleteDir(dIn);
                continue;
            }
            DeleteDir(dIn);
            double D = (b - a) / sp;
            if (!rifeMap.TryGetValue(i, out var m)) rifeMap[i] = m = [];
            m[j] = new RifeInput(clips.Count + extras.Count, W, H);
            extras.Add(new RifeExtra(dOut, nf, D));
        }
        return (pbase + pspan * share, pspan * (1 - share));
    }

    void RemoveQuietly(string p)
    {
        try { fs.Remove(p); } catch (IOException) { }
    }

    long EncodeInner(string output, IReadOnlyList<ExportClip> clips, ExportSettings st, double pbase, double pspan, string tag)
    {
        int n = clips.Count;
        double? ratio = st.Ratio;
        var sizes = clips.Select(c => Geometry.CropSize(c.W, c.H, ratio)).ToList();
        var (refw, refh) = sizes.Aggregate((best, s) => (long)s.W * s.H > (long)best.W * best.H ? s : best);
        double fps = clips.Select(c => c.Fps).Aggregate(Max);
        var durs = clips.Select(c => Timing.Duration(c.GetPieces())).ToList();
        string? xf = null;
        double T = 0.0;
        if (n > 1) (xf, T) = (st.Xfade, st.T);
        if (!string.IsNullOrEmpty(xf))
        {
            T = Min(T, 0.45 * durs.Aggregate(Min));
            if (T < 0.05) (xf, T) = (null, 0.0);
        }
        else xf = null;
        double total = Sum(durs) - (n - 1) * T;
        bool mix = st.MixAudio;
        int nTracks = GraphBuilder.OutTracks(clips, mix);
        string audioK = Text.ResolveAudioK(st.AudioK, clips);       // (2.6: "Original" = the clips' own audio bitrate)
        bool tonemap = st.Tonemap;
        bool muteFast = st.MuteFast;
        var fx = EffectSettings.FromExport(st);
        long audioBps = long.Parse(audioK.TrimEnd('k'), System.Globalization.CultureInfo.InvariantCulture) * 1000 * nTracks;

        if (st.BitrateMbps is { } fixedMbps && fixedMbps != 0)
        {
            double vBps = fixedMbps * 1_000_000;
            double scale = Geometry.ChooseScale(st.ResMode, refw, refh, fps, vBps);
            int W = Geometry.Even(refw * scale), H = Geometry.Even(refh * scale);
            if (fx.SlowRife && !rifeDone) (pbase, pspan) = RifePrepare(clips, W, H, ratio, tonemap, pbase, pspan);
            var graph = GraphBuilder.Build(clips, W, H, ratio, xf, T, fps, fx, tonemap, muteFast, mix, RifeMapView);
            string label = $"{tag}{G(vBps / 1_000_000)} Mbps, {I(W)}x{I(H)}";
            statusCb(label);
            string part = $"{output}.part1.mp4";
            try
            {
                var cmd = Command(part, clips, graph, st.Codec, vBps, audioK, UseHwDec, tonemap);
                var (ok, log) = runner.Run(cmd, total, label, pbase, pspan);
                if (cancelled) throw new ExportCancelledException();
                if (!ok && UseHwDec)
                {
                    UseHwDec = false;
                    cmd = Command(part, clips, graph, st.Codec, vBps, audioK, false, tonemap);
                    (ok, log) = runner.Run(cmd, total, label + " (no hw decode)", pbase, pspan);
                    if (cancelled) throw new ExportCancelledException();
                }
                if (!ok) throw new ExportException("ffmpeg failed:\n" + log);
                long size = fs.GetSize(part);
                fs.Replace(part, output);
                progressCb(pbase + pspan);
                return size;
            }
            finally
            {
                RemoveQuietly(part);
            }
        }

        double targetMb = st.TargetMb ?? throw new ArgumentException("Either TargetMb or BitrateMbps must be set.");
        double targetBytes = targetMb * 1024 * 1024;
        double v = targetBytes * 0.96 * 8 / total - audioBps;
        bool cappedBySource = false;
        var rates = clips.Select(c => c.Bitrate).ToList();
        if (rates.All(r => r > 0) && v > rates.Aggregate(Max)) (v, cappedBySource) = (rates.Aggregate(Max), true);
        if (v < 20_000)
            throw new ExportException("That size is too small for this length of video. Trim it shorter or raise the size limit.");
        const int maxPasses = 6;
        var parts = new List<string>();
        (long size, string path)? best = null;
        double? lo = null, hi = null;
        try
        {
            for (int attempt = 1; attempt <= maxPasses; attempt++)
            {
                if (cancelled) throw new ExportCancelledException();
                v = Max(v, 20_000);
                double scale = Geometry.ChooseScale(st.ResMode, refw, refh, fps, v);
                int W = Geometry.Even(refw * scale), H = Geometry.Even(refh * scale);
                if (fx.SlowRife && !rifeDone) (pbase, pspan) = RifePrepare(clips, W, H, ratio, tonemap, pbase, pspan);
                var graph = GraphBuilder.Build(clips, W, H, ratio, xf, T, fps, fx, tonemap, muteFast, mix, RifeMapView);
                string label = $"{tag}pass {I(attempt)} - {F(v / 1000, 0)} kbps, {I(W)}x{I(H)}";
                statusCb(label);
                string part = $"{output}.part{I(attempt)}.mp4";
                parts.Add(part);
                var cmd = Command(part, clips, graph, st.Codec, v, audioK, UseHwDec, tonemap);
                var (ok, log) = runner.Run(cmd, total, label, pbase, pspan);
                if (cancelled) throw new ExportCancelledException();
                if (!ok && UseHwDec)
                {
                    UseHwDec = false;
                    cmd = Command(part, clips, graph, st.Codec, v, audioK, false, tonemap);
                    (ok, log) = runner.Run(cmd, total, label + " (no hw decode)", pbase, pspan);
                    if (cancelled) throw new ExportCancelledException();
                }
                if (!ok) throw new ExportException("ffmpeg failed:\n" + log);
                long size = fs.GetSize(part);
                if (size <= targetBytes)
                {
                    if (best is null || size > best.Value.size) best = (size, part);
                    lo = lo is null ? v : Max(lo.Value, v);
                    if (size >= 0.90 * targetBytes || cappedBySource) break;
                }
                else hi = hi is null ? v : Min(hi.Value, v);
                // next bitrate: proportional guess, kept inside the known bracket
                double guess = (v + audioBps) * (targetBytes * 0.985 / size) - audioBps;
                if (best is null && attempt >= 3) guess *= 0.92;
                if (lo is not null && hi is not null && !(lo < guess && guess < hi)) guess = (lo.Value + hi.Value) / 2;
                v = guess;
                cappedBySource = false;
            }
            if (best is null)
                throw new ExportException($"Could not get under {G(targetMb)} MB. Lower the size limit or trim the clips shorter.");
            fs.Replace(best.Value.path, output);
            parts.Remove(best.Value.path);
            progressCb(pbase + pspan);
            return best.Value.size;
        }
        finally
        {
            foreach (var p in parts) RemoveQuietly(p);
        }
    }
}
