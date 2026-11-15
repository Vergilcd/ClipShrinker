using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Media;

/// <summary>One video encoder the Codec list can offer. Key is what settings / exports use (it is the ffmpeg encoder
/// name, plus "_hw" for the Media Foundation encoder forced onto the graphics card).</summary>
public sealed record VideoEncoder(string Key, string Label, bool Hevc, string FfmpegName, string Vendor)
{
    /// <summary>Encodes on the graphics card (everything except the Windows CPU encoder).</summary>
    public bool Gpu => Vendor != "Windows" || Key.EndsWith("_hw");
}

/// <summary>2.6: video encoders for every PC. NVENC (NVIDIA) first, then AMF (AMD), Quick Sync (Intel), and Windows
/// Media Foundation on the graphics card or on the CPU (the last resort that works almost anywhere). Which ones
/// really work is found out at start-up with a tiny test encode (Tools.WorkingEncoders).</summary>
public static class Encoders
{
    public static readonly VideoEncoder[] All =
    [
        new("h264_nvenc", "H.264 (h264_nvenc)", false, "h264_nvenc", "NVIDIA"),
        new("hevc_nvenc", "HEVC (hevc_nvenc)", true, "hevc_nvenc", "NVIDIA"),
        new("h264_amf", "H.264 (h264_amf)", false, "h264_amf", "AMD"),
        new("hevc_amf", "HEVC (hevc_amf)", true, "hevc_amf", "AMD"),
        new("h264_qsv", "H.264 (h264_qsv)", false, "h264_qsv", "Intel"),
        new("hevc_qsv", "HEVC (hevc_qsv)", true, "hevc_qsv", "Intel"),
        new("h264_mf_hw", "H.264 (Windows, GPU)", false, "h264_mf", "Windows"),
        new("hevc_mf_hw", "HEVC (Windows, GPU)", true, "hevc_mf", "Windows"),
        new("h264_mf", "H.264 (Windows, CPU)", false, "h264_mf", "Windows"),
        new("hevc_mf", "HEVC (Windows, CPU)", true, "hevc_mf", "Windows"),
    ];

    public static IReadOnlyList<string> Labels { get; } = All.Select(e => e.Label).ToList();

    public static VideoEncoder? ByKey(string key) => All.FirstOrDefault(e => e.Key == key);
    public static VideoEncoder? ByLabel(string label) => All.FirstOrDefault(e => e.Label == label);

    /// <summary>The settings label -> encoder key. Unknown labels keep the 1.x / 2.x rule (HEVC or H.264 on NVENC).</summary>
    public static string KeyOf(string label) => ByLabel(label)?.Key ?? (label.Contains("HEVC") ? "hevc_nvenc" : "h264_nvenc");

    /// <summary>The label to use when this PC cannot run the saved one: the first working encoder of the same kind
    /// (HEVC / H.264) in the order above, else the first working one at all. Unchanged when it works (or nothing does).</summary>
    public static string Fallback(string label, IReadOnlyCollection<string> workingKeys)
    {
        var cur = ByLabel(label) ?? ByKey(KeyOf(label))!;
        if (workingKeys.Count == 0 || workingKeys.Contains(cur.Key)) return cur.Label;
        var ok = All.Where(e => workingKeys.Contains(e.Key)).ToList();
        return (ok.FirstOrDefault(e => e.Hevc == cur.Hevc) ?? ok[0]).Label;
    }

    /// <summary>The best choice among the offered encoders: the GPU maker's own HEVC encoder (smallest files for the
    /// size), then its H.264, then Windows on the GPU, then the CPU.</summary>
    public static string? Recommended(IReadOnlyCollection<string> offered) =>
        new[] { "hevc_nvenc", "hevc_amf", "hevc_qsv", "h264_nvenc", "h264_amf", "h264_qsv", "hevc_mf_hw", "h264_mf_hw", "h264_mf", "hevc_mf" }
            .FirstOrDefault(offered.Contains);

    /// <summary>2.6: the short hint after a codec in the open Codec list, so a new user knows which one to pick.</summary>
    public static string Hint(string key, IReadOnlyCollection<string> offered)
    {
        var e = ByKey(key);
        if (e is null) return "";
        bool rec = Recommended(offered) == key;
        bool native = e.Vendor != "Windows";
        bool nativeThere = offered.Any(k => ByKey(k)?.Vendor is "NVIDIA" or "AMD" or "Intel");
        if (native)
            return rec ? $"recommended for your {e.Vendor} GPU"
                 : e.Hevc ? $"{e.Vendor} GPU, smaller files" : $"{e.Vendor} GPU, plays on older devices";
        if (e.Gpu)
            return rec ? "recommended for this PC (Windows encoder)"
                 : nativeThere ? "Windows encoder, lower quality" : "Windows encoder, plays on older devices";
        return rec ? "slow, no GPU encoder works here" : "CPU only, slow";
    }

    /// <summary>ffmpeg output options for the video at vBps (bits/s). NVENC keeps the 1.0.8 options exactly; the others
    /// aim at the same average bitrate (the export's size passes correct what they miss).</summary>
    public static List<string> Args(string key, double vBps)
    {
        long kb = Math.Max(20, Int(vBps / 1000));
        string b = $"{I(kb)}k", buf = $"{I(kb * 2)}k";
        var e = ByKey(key) ?? ByKey("hevc_nvenc")!;
        var a = e.Vendor switch
        {
            "NVIDIA" => new List<string> { "-c:v", e.FfmpegName, "-preset", "p5", "-tune", "hq", "-rc", "vbr",
                                           "-b:v", b, "-maxrate", b, "-bufsize", buf, "-spatial-aq", "1", "-pix_fmt", "yuv420p" },
            "AMD" => ["-c:v", e.FfmpegName, "-usage", "transcoding", "-quality", "quality", "-rc", "vbr_peak",
                      "-b:v", b, "-maxrate", b, "-bufsize", buf, "-pix_fmt", "yuv420p"],
            "Intel" => ["-c:v", e.FfmpegName, "-preset", "slower", "-b:v", b, "-maxrate", b, "-bufsize", buf, "-pix_fmt", "nv12"],
            _ => ["-c:v", e.FfmpegName, "-rate_control", "cbr", "-b:v", b, "-pix_fmt", "nv12",
                  .. (e.Key.EndsWith("_hw") ? new[] { "-hw_encoding", "1" } : [])],
        };
        if (e.Hevc) a.AddRange(["-tag:v", "hvc1"]);
        return a;
    }

    /// <summary>A half-second test encode of a grey picture (no input file needed); exit code 0 = it works here.</summary>
    public static List<string> ProbeCommand(string ffmpeg, string key) =>
        [ffmpeg, "-hide_banner", "-v", "error", "-filter_complex", "color=c=gray:s=320x240:r=30:d=0.5,format=yuv420p",
         .. Args(key, 1_000_000), "-f", "null", "-"];

    /// <summary>For testing on one PC: CLIPSHRINKER_HIDE_ENCODERS=nvenc,cuda (any parts of keys) makes those count as
    /// not working, so the AMD / Intel / Windows paths can be tried on an NVIDIA machine.</summary>
    public static bool HiddenForTest(string key) =>
        (Environment.GetEnvironmentVariable("CLIPSHRINKER_HIDE_ENCODERS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(key.Contains);
}
