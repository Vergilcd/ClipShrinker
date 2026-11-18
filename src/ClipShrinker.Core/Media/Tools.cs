using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ClipShrinker.Core.Ffmpeg;

namespace ClipShrinker.Core.Media;

/// <summary>Finds ffmpeg / ffprobe and runs short helper commands (reference find_tool, probe, _check_tools).</summary>
public sealed class Tools
{
    public string? Ffmpeg { get; }
    public string? Ffprobe { get; }

    public Tools(string? ffmpeg, string? ffprobe)
    {
        Ffmpeg = ffmpeg;
        Ffprobe = ffprobe;
    }

    /// <summary>name.exe in one of the folders (first match), else on PATH (reference find_tool).</summary>
    public static string? Find(string name, params string?[] folders)
    {
        foreach (var d in folders)
        {
            if (string.IsNullOrEmpty(d)) continue;
            var p = Path.Combine(d, name + ".exe");
            if (File.Exists(p)) return Path.GetFullPath(p);
        }
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            try
            {
                var p = Path.Combine(d.Trim('"'), name + ".exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static Tools Locate(params string?[] folders) => new(Find("ffmpeg", folders), Find("ffprobe", folders));

    /// <summary>Runs a command to the end; returns exit code, stdout and stderr (reference run_capture).</summary>
    public static (int Code, string Out, string Err) Capture(IReadOnlyList<string> cmd)
    {
        var psi = new ProcessStartInfo(cmd[0])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, err.Result);
    }

    /// <summary>True when this ffmpeg has the h264_nvenc encoder (the app warns otherwise).</summary>
    public bool HasNvenc()
    {
        if (Ffmpeg is null) return false;
        try { return Capture([Ffmpeg, "-hide_banner", "-encoders"]).Out.Contains("h264_nvenc"); }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>True when this ffmpeg has the named filter (the slim build leaves many out). Asked once per name.</summary>
    public bool HasFilter(string name)
    {
        if (Ffmpeg is null) return false;
        lock (filterCache)
            if (filterCache.TryGetValue(name, out var known)) return known;
        bool has;
        try
        {
            has = Capture([Ffmpeg, "-hide_banner", "-filters"]).Out.Split('\n')
                .Any(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } p && p[1] == name);
        }
        catch (System.ComponentModel.Win32Exception) { has = false; }
        lock (filterCache) filterCache[name] = has;
        return has;
    }

    readonly Dictionary<string, bool> filterCache = [];

    /// <summary>2.7: length in seconds of a sound file (Sound track module); throws MediaException when it has no sound.</summary>
    public double ProbeSoundDuration(string path)
    {
        if (Ffprobe is null) throw new MediaException("ffprobe.exe was not found.");
        var (code, output, err) = Capture([Ffprobe, "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_type:format=duration",
                                           "-of", "json", path]);
        if (code != 0) throw new MediaException(err.Trim() is { Length: > 0 } e ? e : "ffprobe failed");
        var root = JsonDocument.Parse(output).RootElement;
        if (!root.TryGetProperty("streams", out var st) || st.GetArrayLength() == 0) throw new MediaException("No sound found in this file.");
        string? d = root.TryGetProperty("format", out var f) && f.TryGetProperty("duration", out var dv) ? dv.GetString() : null;
        return d is not null && Py.Py.ParseFloat(d) is { } x && x > 0 && double.IsFinite(x) ? x : throw new MediaException("Unknown length.");
    }

    /// <summary>2.7: the loudness outline of a sound file for its waveform: the peak (0..1) of every 1/perSecond s,
    /// from a decode to 2 kHz mono (fast, and plenty for a small drawing).</summary>
    public float[] SoundPeaks(string path, int perSecond = 50)
    {
        if (Ffmpeg is null) return [];
        var psi = new ProcessStartInfo(Ffmpeg)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-nostdin", "-i", path, "-vn", "-ac", "1", "-ar", "2000", "-c:a", "pcm_s16le", "-f", "s16le", "-" })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        _ = p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        var bytes = ms.GetBuffer();
        int n = (int)ms.Length / 2, step = Math.Max(1, 2000 / perSecond);
        var peaks = new float[(n + step - 1) / step];
        for (int i = 0; i < n; i++)
        {
            float v = Math.Abs(BitConverter.ToInt16(bytes, i * 2) / 32768f);
            if (v > peaks[i / step]) peaks[i / step] = v;
        }
        return peaks;
    }

    /// <summary>2.6: the encoder keys (Encoders.All) that really encode on this PC: each one gets a tiny test encode,
    /// all at once (~1 s). An encoder missing from this ffmpeg build fails its test like one without the hardware.</summary>
    public IReadOnlyList<string> WorkingEncoders()
    {
        if (Ffmpeg is null) return [];
        var ok = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(Encoders.All, e =>
        {
            if (Encoders.HiddenForTest(e.Key)) return;
            try { if (Capture(Encoders.ProbeCommand(Ffmpeg, e.Key)).Code == 0) ok.Add(e.Key); }
            catch (System.ComponentModel.Win32Exception) { }
        });
        return Encoders.All.Where(e => ok.Contains(e.Key)).Select(e => e.Key).ToList();
    }

    /// <summary>2.6: NVIDIA (CUDA) decoding works here. Without it the export and the preview decode on the CPU from the
    /// start instead of failing once and retrying.</summary>
    public bool CudaWorks()
    {
        if (Ffmpeg is null || Encoders.HiddenForTest("cuda")) return false;
        try { return Capture([Ffmpeg, "-hide_banner", "-v", "error", "-init_hw_device", "cuda=c", "-filter_complex", "color=s=64x64:d=0.04",
                              "-c:v", "rawvideo", "-f", "null", "-"]).Code == 0; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>2.5: GPU denoise works here: this ffmpeg has nlmeans_opencl and a tiny test picture goes through it
    /// on an OpenCL device (graphics driver with OpenCL). Takes ~0.3 s; ask once and keep the answer.</summary>
    public bool OpenClWorks()
    {
        if (Ffmpeg is null || Encoders.HiddenForTest("opencl") || !HasFilter("nlmeans_opencl")) return false;
        try
        {
            return Capture([Ffmpeg, "-hide_banner", "-v", "error", .. Modules.PictureAdjust.OpenClArgs,
                            "-filter_complex", "color=s=64x64:d=0.04," + Modules.PictureAdjust.DenoiseFilter(50, gpu: true),
                            "-c:v", "rawvideo", "-f", "null", "-"]).Code == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>ffprobe a file (reference probe()). Throws MediaException with ffprobe's message.</summary>
    public MediaInfo Probe(string path)
    {
        if (Ffprobe is null) throw new MediaException("ffprobe.exe was not found.");
        var (code, output, err) = Capture([Ffprobe, "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path]);
        if (code != 0) throw new MediaException(err.Trim() is { Length: > 0 } e ? e : "ffprobe failed");
        return ParseProbe(output, () => new FileInfo(path).Length);
    }

    /// <summary>The probe() parsing, separate so it can be tested with saved ffprobe output.</summary>
    public static MediaInfo ParseProbe(string json, Func<long> fileSize)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var streams = root.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
        string Type(JsonElement x) => x.TryGetProperty("codec_type", out var t) ? t.GetString() ?? "" : "";
        var v = streams.FirstOrDefault(x => Type(x) == "video");
        if (v.ValueKind != JsonValueKind.Object) throw new MediaException("No video stream found.");
        int naudio = Math.Min(C.MaxAudio, streams.Count(x => Type(x) == "audio"));
        string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
        // Python "x or y": an empty string counts as missing
        string? Or(params string?[] xs) => xs.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        var fmt = root.GetProperty("format");
        string? trc = Str(v, "color_transfer");
        double ParseNum(string? x) => x is null ? 0 : Py.Py.ParseFloat(x) ?? throw new MediaException($"could not convert string to float: '{x}'");
        return new MediaInfo(
            W: v.GetProperty("width").GetInt32(),
            H: v.GetProperty("height").GetInt32(),
            Fps: Geometry.ParseFps(Or(Str(v, "avg_frame_rate"), Str(v, "r_frame_rate") ?? "30/1")),
            Dur: ParseNum(Or(Str(fmt, "duration"), Str(v, "duration"))),
            NAudio: naudio,
            Bitrate: ParseNum(Or(Str(fmt, "bit_rate"))),
            Size: Or(Str(fmt, "size")) is { } sz ? long.Parse(sz) : fileSize(),
            Trc: trc is "smpte2084" or "arib-std-b67" ? trc : null,
            Prim: Str(v, "color_primaries"),
            Space: Str(v, "color_space"),
            // 2.6: the first audio track's bitrate, for the "Original" audio setting (0 = not known)
            AudioBitrate: streams.FirstOrDefault(x => Type(x) == "audio") is { ValueKind: JsonValueKind.Object } a0
                          && Py.Py.ParseFloat(Str(a0, "bit_rate") ?? "") is { } ab && !double.IsNaN(ab) ? ab : 0);
    }
}

public sealed class MediaException(string message) : Exception(message);
