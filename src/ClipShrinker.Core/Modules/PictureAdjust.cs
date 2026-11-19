using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Modules;

/// <summary>The "Picture adjustments" module: denoise, brightness, contrast and saturation as one per-clip video filter
/// (after crop / scale / tone-mapping). Neutral values give no filter at all, so exports stay identical.
/// Denoise (2.5.0) uses nlmeans: hqdn3d is GPL-only and cannot go in the LGPL build; atadenoise smeared moving footage.</summary>
public static class PictureAdjust
{
    public const int Min = -100, Max = 100;

    public static int Clamp(int v) => Math.Max(Min, Math.Min(Max, v));

    /// <summary>"25", "+25%", "-40 %" -> the value (clamped to -100 .. 100); null when it is not a number.</summary>
    public static int? ParsePercent(string? text)
    {
        string t = (text ?? "").Trim().TrimEnd('%').Trim().Replace(',', '.');
        if (t.Length == 0) return 0;
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
               && !double.IsNaN(v) && !double.IsInfinity(v)
            ? Clamp((int)Math.Round(Math.Max(-1e6, Math.Min(1e6, v)), MidpointRounding.AwayFromZero)) : null;
    }

    /// <summary>+25% / -40% / 0%.</summary>
    public static string FormatPercent(int v) => v > 0 ? $"+{I(v)}%" : $"{I(v)}%";

    /// <summary>The filter for these values, or null when nothing changes the picture.
    /// Brightness / contrast / saturation are percentages (-100 .. 100, 0 = unchanged).</summary>
    public static string? Filter(int brightness, int contrast, int saturation) => Filter(brightness, contrast, saturation, 0, 0);

    /// <summary>2.8: with white balance. temperature: + warmer (more orange), - cooler (more blue); tint: + magenta,
    /// - green (-100 .. 100, 0 = unchanged). Both shift the colour planes (U = blue-yellow, V = red-cyan) in the same
    /// lutyuv, so 10-bit clips stay 10-bit and the neutral picture still gives no filter.</summary>
    public static string? Filter(int brightness, int contrast, int saturation, int temperature, int tint)
    {
        int b = Clamp(brightness), c = Clamp(contrast), s = Clamp(saturation), t = Clamp(temperature), g = Clamp(tint);
        if (b == 0 && c == 0 && s == 0 && t == 0 && g == 0) return null;
        // lutyuv works in the video's own value range (minval / maxval), so 8- and 10-bit clips get the same look.
        // Brightness: +-100 % moves the picture by 30 % of the range; contrast / saturation: 0 .. 2x around the middle.
        // Temperature: +-100 % moves U and V by 6 % of the range in opposite directions; tint: both by 5 %, same way.
        const string mid = "(minval+maxval)/2";
        string Y = c == 0 && b == 0 ? "val"
            : $"clip((val-{mid})*{F(1 + c / 100.0, 3)}+{mid}+{F(0.3 * b / 100.0, 3)}*(maxval-minval),minval,maxval)";
        string Chroma(double shift) => s == 0 && shift == 0 ? "val"
            : shift == 0 ? $"clip((val-{mid})*{F(1 + s / 100.0, 3)}+{mid},minval,maxval)"
            : s == 0 ? $"clip(val+{F(shift, 4)}*(maxval-minval),minval,maxval)"
            : $"clip((val-{mid})*{F(1 + s / 100.0, 3)}+{mid}+{F(shift, 4)}*(maxval-minval),minval,maxval)";
        double du = -0.06 * t / 100.0 + 0.05 * g / 100.0, dv = 0.06 * t / 100.0 + 0.05 * g / 100.0;
        return $"lutyuv=y='{Y}':u='{Chroma(du)}':v='{Chroma(dv)}'";
    }
    public const int DenoiseMax = 100;

    public static int ClampDenoise(int v) => Math.Max(0, Math.Min(DenoiseMax, v));

    /// <summary>"25", "25%" -> 0 .. 100 (negative numbers count as 0); null when it is not a number.</summary>
    public static int? ParseDenoise(string? text) => ParsePercent(text) is { } v ? ClampDenoise(v) : null;

    /// <summary>Denoise as shown in its box: 0% .. 100% (no plus sign).</summary>
    public static string FormatDenoise(int v) => $"{I(ClampDenoise(v))}%";

    /// <summary>The denoise filter (0 = none): non-local means, strength 1 .. 5.5. On the graphics card through OpenCL
    /// (~5x faster; 8-bit only) when it works on this PC, else the same filter on the CPU. Small patch / search window
    /// (3 / 5) keeps it fast enough for the preview; 2.5.0 timings on a 1080p60 clip: GPU ~46 fps, CPU ~9 fps.</summary>
    public static string? DenoiseFilter(int denoise, bool gpu)
    {
        int d = ClampDenoise(denoise);
        if (d == 0) return null;
        string s = F(1 + 4.5 * d / 100.0, 2);   // 2.5.0 test: above the old 50 % (s = 5.5) it only blurred, so 100 % = 5.5
        return gpu ? $"format=yuv420p,hwupload,nlmeans_opencl=s={s}:p=3:r=5,hwdownload,format=yuv420p"
                   : $"nlmeans=s={s}:p=3:r=5";
    }

    /// <summary>Denoise first (it works best on the untouched picture), then the colour. hdrKept: the clip stays HDR
    /// (10-bit, "Convert HDR" off); denoise is skipped there because both filters would make it 8-bit.</summary>
    public static string? Filter(int brightness, int contrast, int saturation, int denoise, bool gpu, bool hdrKept = false) =>
        Filter(brightness, contrast, saturation, 0, 0, denoise, gpu, hdrKept);

    public static string? Filter(int brightness, int contrast, int saturation, int temperature, int tint, int denoise, bool gpu, bool hdrKept = false)
    {
        var parts = new[] { hdrKept ? null : DenoiseFilter(denoise, gpu), Filter(brightness, contrast, saturation, temperature, tint) }
            .Where(p => p is not null).ToList();
        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    /// <summary>The filter runs on the graphics card: ffmpeg then needs an OpenCL device (global options).</summary>
    public static bool UsesOpenCl(string? vf) => vf is not null && vf.Contains("nlmeans_opencl");

    public static readonly string[] OpenClArgs = ["-init_hw_device", "opencl=ocl", "-filter_hw_device", "ocl"];

    /// <summary>OpenClArgs when any of these filters needs it, else nothing.</summary>
    public static IEnumerable<string> HwArgs(IEnumerable<string?> vfs) => vfs.Any(UsesOpenCl) ? OpenClArgs : [];
}
