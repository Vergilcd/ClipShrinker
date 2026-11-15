using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Plugins;

/// <summary>The "Picture adjustments" plugin: brightness, contrast and saturation as one per-clip video filter
/// (after crop / scale / tone-mapping). Neutral values give no filter at all, so exports stay identical.
/// (Denoise is planned for 2.5.0: it needs the hqdn3d filter, which the slim ffmpeg build does not have yet.)</summary>
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
    public static string? Filter(int brightness, int contrast, int saturation)
    {
        int b = Clamp(brightness), c = Clamp(contrast), s = Clamp(saturation);
        if (b == 0 && c == 0 && s == 0) return null;
        // lutyuv works in the video's own value range (minval / maxval), so 8- and 10-bit clips get the same look.
        // Brightness: +-100 % moves the picture by 30 % of the range; contrast / saturation: 0 .. 2x around the middle.
        const string mid = "(minval+maxval)/2";
        string Y = c == 0 && b == 0 ? "val"
            : $"clip((val-{mid})*{F(1 + c / 100.0, 3)}+{mid}+{F(0.3 * b / 100.0, 3)}*(maxval-minval),minval,maxval)";
        string UV = s == 0 ? "val" : $"clip((val-{mid})*{F(1 + s / 100.0, 3)}+{mid},minval,maxval)";
        return $"lutyuv=y='{Y}':u='{UV}':v='{UV}'";
    }
}
