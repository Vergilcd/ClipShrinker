using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

namespace ClipShrinker.Core.Analysis;

/// <summary>One moment that stands out in a clip: a loud peak or a sudden picture change.</summary>
public sealed record Moment(double Time, string Kind, double Strength);

/// <summary>Brightness and colour of a clip, from small sample frames (0..255 luma, 0..1 saturation).</summary>
public sealed record PictureStats(double MeanLuma, double DarkLuma, double BrightLuma, double MeanSat, int Frames);

/// <summary>
/// Dev (MCP research, docs/mcp-agent-research.md): looks at a clip for an agent. Everything is computed here from
/// ffmpeg's raw output, because the bundled slim ffmpeg has no scdet / ebur128 / signalstats / tile / drawtext.
/// </summary>
public static class ClipAnalysis
{
    // ------------------------------------------------------------------ raw decoding
    /// <summary>Frames from..to, one every `every` seconds, as w x h RGB24 (tone-mapped when trc is set).
    /// Frame i is at about from + i * every.</summary>
    public static List<byte[]> RawFrames(string ffmpeg, string path, double from, double to, double every, int w, int h,
                                         string? trc, bool gray = false)
    {
        string vf = FormattableString.Invariant($"fps=1/{every:0.######},scale={w}:{h}");
        if (trc is not null) vf += "," + Geometry.TonemapChain(trc);
        vf += gray ? ",format=gray" : ",format=rgb24";
        var bytes = Run(ffmpeg, ["-v", "error", "-nostdin", "-ss", F(from), "-t", F(Math.Max(0.05, to - from)), "-i", path,
                                 "-an", "-vf", vf, "-f", "rawvideo", "-"]);
        int size = w * h * (gray ? 1 : 3);
        var res = new List<byte[]>();
        for (int o = 0; o + size <= bytes.Length; o += size) res.Add(bytes.AsSpan(o, size).ToArray());
        return res;
    }

    /// <summary>Frames at the given times, one short ffmpeg run per time (4 at once): fast for a few frames spread over
    /// a long recording, where decoding everything would take minutes. Missing frames are null.</summary>
    public static byte[]?[] SampleFrames(string ffmpeg, string path, IReadOnlyList<double> times, int w, int h, string? trc)
    {
        string vf = FormattableString.Invariant($"scale={w}:{h}");
        if (trc is not null) vf += "," + Geometry.TonemapChain(trc);
        vf += ",format=rgb24";
        var res = new byte[]?[times.Count];
        Parallel.For(0, times.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var b = Run(ffmpeg, ["-v", "error", "-nostdin", "-ss", F(times[i]), "-i", path, "-an", "-frames:v", "1",
                                 "-vf", vf, "-f", "rawvideo", "-"]);
            res[i] = b.Length >= w * h * 3 ? b.AsSpan(0, w * h * 3).ToArray() : null;
        });
        return res;
    }

    /// <summary>Two tiny grey frames a second over from..to, for picture changes. A long recording is decoded in 4 parts
    /// at once, on the NVIDIA card when it can (a 15-minute 1080p60 PS5 clip: minutes on the CPU).</summary>
    public static List<byte[]> MotionFrames(string ffmpeg, string path, double from, double to, bool cuda)
    {
        const int w = 32, h = 18;
        int parts = to - from > 120 ? 4 : 1;
        double len = (to - from) / parts;
        len = Math.Ceiling(len * 2) / 2;                                   // whole half-seconds: the parts line up
        var chunks = new List<byte[]>[parts];
        Parallel.For(0, parts, new ParallelOptions { MaxDegreeOfParallelism = parts }, k =>
        {
            double a = from + k * len, b = Math.Min(to, a + len);
            var args = new List<string> { "-v", "error", "-nostdin" };
            if (cuda) args.AddRange(["-hwaccel", "cuda"]);
            args.AddRange(["-ss", F(a), "-t", F(Math.Max(0.05, b - a)), "-i", path, "-an",
                           "-vf", $"fps=2,scale={w}:{h},format=gray", "-f", "rawvideo", "-"]);
            var bytes = Run(ffmpeg, args);
            var list = new List<byte[]>();
            for (int o = 0; o + w * h <= bytes.Length && list.Count < (int)Math.Round((b - a) * 2); o += w * h) list.Add(bytes.AsSpan(o, w * h).ToArray());
            chunks[k] = list;
        });
        return [.. chunks.SelectMany(c => c)];
    }

    /// <summary>The first audio track as mono 16-bit PCM at the given rate, from..to.</summary>
    public static short[] RawAudio(string ffmpeg, string path, double from, double to, int rate = 8000)
    {
        var bytes = Run(ffmpeg, ["-v", "error", "-nostdin", "-ss", F(from), "-t", F(Math.Max(0.05, to - from)), "-i", path,
                                 "-vn", "-map", "0:a:0?", "-ac", "1", "-ar", rate.ToString(CultureInfo.InvariantCulture),
                                 "-c:a", "pcm_s16le", "-f", "s16le", "-"]);
        var s = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, s, 0, s.Length * 2);
        return s;
    }

    static byte[] Run(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        _ = p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        return ms.ToArray();
    }

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>An even size of the given width that keeps the picture's shape.</summary>
    public static (int W, int H) Fit(int srcW, int srcH, int w)
    {
        w -= w % 2;
        int h = (int)Math.Round(w * (double)srcH / Math.Max(1, srcW));
        return (w, Math.Max(2, h - h % 2));
    }

    // ------------------------------------------------------------------ contact sheet
    /// <summary>A PNG with the frames in a grid, each stamped with its number and time (from the clip's start).
    /// Returns the PNG and the time of each frame.</summary>
    public static (byte[] Png, List<double> Times) ContactSheet(string ffmpeg, string path, int srcW, int srcH, string? trc,
                                                                double from, double to, double every, int cols = 4, int cellW = 256)
    {
        var (cw, ch) = Fit(srcW, srcH, cellW);
        var want = new List<double>();
        for (double t = from; t <= to - 0.05 && want.Count < 100; t += every) want.Add(t);
        var got = SampleFrames(ffmpeg, path, want, cw, ch, trc);
        var frames = new List<byte[]>();
        var times = new List<double>();
        for (int i = 0; i < got.Length; i++) if (got[i] is { } f) { frames.Add(f); times.Add(want[i]); }
        cols = Math.Max(1, Math.Min(cols, Math.Max(1, frames.Count)));
        int rows = Math.Max(1, (frames.Count + cols - 1) / cols);
        const int gap = 4;
        int W = cols * cw + (cols + 1) * gap, H = rows * ch + (rows + 1) * gap;
        var img = new byte[W * H * 3];                                       // black background
        for (int i = 0; i < frames.Count; i++)
        {
            int x0 = gap + i % cols * (cw + gap), y0 = gap + i / cols * (ch + gap);
            for (int y = 0; y < ch; y++)
                Buffer.BlockCopy(frames[i], y * cw * 3, img, ((y0 + y) * W + x0) * 3, cw * 3);
            Label(img, W, x0 + 3, y0 + 3, $"#{i + 1} {Clock(times[i])}");
        }
        return (Png.Encode(img, W, H), times);
    }

    /// <summary>m:ss.s (or h:mm:ss.s).</summary>
    public static string Clock(double t)
    {
        t = Math.Max(0, t);
        int h = (int)(t / 3600), m = (int)(t % 3600 / 60);
        double s = t % 60;
        string ss = s.ToString("00.0", CultureInfo.InvariantCulture);
        return h > 0 ? $"{h}:{m:00}:{ss}" : $"{m}:{ss}";
    }

    /// <summary>Text in a small 3x5 pixel font, doubled, white on a black box (digits, '#', ':', '.').</summary>
    static void Label(byte[] img, int W, int x, int y, string text)
    {
        const int sc = 2, cw = 4 * sc;
        int bw = text.Length * cw + 2 * sc, bh = 7 * sc;
        int H = img.Length / 3 / W;
        for (int yy = y; yy < Math.Min(H, y + bh); yy++)
            for (int xx = x; xx < Math.Min(W, x + bw); xx++)
                img[(yy * W + xx) * 3] = img[(yy * W + xx) * 3 + 1] = img[(yy * W + xx) * 3 + 2] = 0;
        for (int k = 0; k < text.Length; k++)
        {
            if (!Font.TryGetValue(text[k], out var rows)) continue;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 3; c++)
                {
                    if ((rows[r] >> (2 - c) & 1) == 0) continue;
                    for (int dy = 0; dy < sc; dy++)
                        for (int dx = 0; dx < sc; dx++)
                        {
                            int px = x + sc + k * cw + c * sc + dx, py = y + sc + r * sc + dy;
                            if (px < W && py < H) img[(py * W + px) * 3] = img[(py * W + px) * 3 + 1] = img[(py * W + px) * 3 + 2] = 255;
                        }
                }
        }
    }

    static readonly Dictionary<char, int[]> Font = new()
    {
        ['0'] = [7, 5, 5, 5, 7], ['1'] = [2, 6, 2, 2, 7], ['2'] = [7, 1, 7, 4, 7], ['3'] = [7, 1, 7, 1, 7],
        ['4'] = [5, 5, 7, 1, 1], ['5'] = [7, 4, 7, 1, 7], ['6'] = [7, 4, 7, 5, 7], ['7'] = [7, 1, 1, 1, 1],
        ['8'] = [7, 5, 7, 5, 7], ['9'] = [7, 5, 7, 1, 7], [':'] = [0, 2, 0, 2, 0], ['.'] = [0, 0, 0, 0, 2],
        ['#'] = [5, 7, 5, 7, 5], [' '] = [0, 0, 0, 0, 0],
    };

    // ------------------------------------------------------------------ moments
    /// <summary>Loud peaks (from the sound, in 0.1 s steps) and sudden picture changes (2 frames a second), strongest
    /// first, at most `max`, at least 2 s apart. Strength: dB above the clip's typical level / change score.</summary>
    public static List<Moment> Moments(string ffmpeg, string path, bool hasAudio, double from, double to, bool cuda, int max = 12)
    {
        var res = new List<Moment>();
        if (hasAudio) res.AddRange(LoudPeaks(RawAudio(ffmpeg, path, from, to), 8000, from));
        res.AddRange(PictureChanges(MotionFrames(ffmpeg, path, from, to, cuda), 0.5, from));
        return Pick(res, max);
    }

    /// <summary>The strongest moments, at least 2 s apart within a kind. The kinds have different scales (dB vs. times
    /// the usual motion), so each kind gets half of the places; what one kind leaves free goes to the other.</summary>
    public static List<Moment> Pick(IEnumerable<Moment> all, int max)
    {
        var perKind = all.GroupBy(m => m.Kind).ToDictionary(g => g.Key, g =>
        {
            var keep = new List<Moment>();
            foreach (var m in g.OrderByDescending(m => m.Strength))
                if (!keep.Any(p => Math.Abs(p.Time - m.Time) < 2)) keep.Add(m);
            return keep;
        });
        int share = Math.Max(1, max / Math.Max(1, perKind.Count));
        var picked = perKind.Values.SelectMany(l => l.Take(share)).ToList();
        foreach (var m in perKind.Values.SelectMany(l => l.Skip(share)).OrderByDescending(m => m.Strength))
            if (picked.Count < max) picked.Add(m);
        return [.. picked.Take(max).OrderBy(m => m.Time)];
    }

    /// <summary>Loudness in dBFS of every 0.1 s window.</summary>
    public static double[] LoudnessDb(short[] pcm, int rate)
    {
        int win = Math.Max(1, rate / 10);
        var db = new double[(pcm.Length + win - 1) / win];
        for (int k = 0; k < db.Length; k++)
        {
            double sum = 0; int n = 0;
            for (int i = k * win; i < Math.Min(pcm.Length, (k + 1) * win); i++) { double v = pcm[i] / 32768.0; sum += v * v; n++; }
            db[k] = n == 0 ? -100 : 10 * Math.Log10(Math.Max(1e-10, sum / n));
        }
        return db;
    }

    /// <summary>Windows at least 8 dB above the median loudness that are the loudest within 1 s around them.</summary>
    public static List<Moment> LoudPeaks(short[] pcm, int rate, double from)
    {
        var db = LoudnessDb(pcm, rate);
        if (db.Length < 3) return [];
        double median = db.OrderBy(x => x).ElementAt(db.Length / 2);
        var res = new List<Moment>();
        for (int k = 0; k < db.Length; k++)
        {
            if (db[k] < median + 8) continue;
            bool top = true;
            for (int j = Math.Max(0, k - 5); j <= Math.Min(db.Length - 1, k + 5) && top; j++) if (db[j] > db[k]) top = false;
            if (top) res.Add(new Moment(from + k * 0.1, "loud", Math.Round(db[k] - median, 1)));
        }
        return res;
    }

    /// <summary>Frame-to-frame difference of tiny grey frames; a change well above the clip's usual motion counts.</summary>
    public static List<Moment> PictureChanges(List<byte[]> gray, double every, double from)
    {
        if (gray.Count < 3) return [];
        var d = new double[gray.Count];
        for (int i = 1; i < gray.Count; i++)
        {
            long s = 0;
            for (int j = 0; j < gray[i].Length; j++) s += Math.Abs(gray[i][j] - gray[i - 1][j]);
            d[i] = s / (double)gray[i].Length;
        }
        var rest = d.Skip(1).OrderBy(x => x).ToArray();
        double median = rest[rest.Length / 2], mad = rest.Select(x => Math.Abs(x - median)).OrderBy(x => x).ElementAt(rest.Length / 2);
        double limit = median + Math.Max(6, 4 * mad);
        var res = new List<Moment>();
        for (int i = 1; i < d.Length; i++)
            if (d[i] >= limit && d[i] >= d[i - 1] && (i + 1 >= d.Length || d[i] >= d[i + 1]))
                res.Add(new Moment(from + i * every, "picture change", Math.Round(d[i] / Math.Max(1, median), 1)));
        return res;
    }

    // ------------------------------------------------------------------ picture statistics
    public static PictureStats Picture(string ffmpeg, string path, string? trc, double from, double to)
    {
        int n = 24;
        var times = Enumerable.Range(0, n).Select(i => from + (i + 0.5) * (to - from) / n).ToList();
        return Stats([.. SampleFrames(ffmpeg, path, times, 64, 36, trc).OfType<byte[]>()]);
    }

    public static PictureStats Stats(List<byte[]> rgbFrames)
    {
        var lum = new List<double>();
        double sat = 0;
        foreach (var f in rgbFrames)
            for (int i = 0; i + 2 < f.Length; i += 3)
            {
                double r = f[i], g = f[i + 1], b = f[i + 2];
                lum.Add(0.2126 * r + 0.7152 * g + 0.0722 * b);
                double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                sat += mx <= 0 ? 0 : (mx - mn) / mx;
            }
        if (lum.Count == 0) return new PictureStats(0, 0, 0, 0, 0);
        lum.Sort();
        return new PictureStats(Math.Round(lum.Average(), 1), Math.Round(lum[lum.Count * 5 / 100], 1),
                                Math.Round(lum[lum.Count * 95 / 100], 1), Math.Round(sat / lum.Count, 3), rgbFrames.Count);
    }
}

/// <summary>A minimal PNG writer (RGB, 8 bit, no filtering).</summary>
public static class Png
{
    public static byte[] Encode(byte[] rgb, int w, int h)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
            for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgb, y * w * 3, w * 3); }
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", raw.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, td, 4, data.Length);
        s.Write(td);
        var crc = new byte[4]; BE(crc, 0, (int)Crc(td)); s.Write(crc);
    }

    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(byte[] b)
    {
        uint c = 0xFFFFFFFF;
        foreach (var x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
