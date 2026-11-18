using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Modules;

/// <summary>2.6: the "Sound equalizer" module: five bands and an overall volume in dB, one audio filter for the export
/// (every output track) and the preview sound. Neutral values give no filter, so exports stay identical.</summary>
public static class Equalizer
{
    /// <summary>Band centres in Hz (each band is 1.5 octaves wide).</summary>
    public static readonly int[] Bands = [60, 250, 1000, 4000, 12000];
    public const int BandMax = 12;      // +-12 dB per band
    public const int VolumeMax = 20;    // +-20 dB overall

    public static int ClampBand(int v) => Math.Max(-BandMax, Math.Min(BandMax, v));
    public static int ClampVolume(int v) => Math.Max(-VolumeMax, Math.Min(VolumeMax, v));

    /// <summary>"60 Hz", "1 kHz", "12 kHz".</summary>
    public static string BandName(int hz) => hz >= 1000 ? $"{I(hz / 1000)} kHz" : $"{I(hz)} Hz";

    /// <summary>"3", "+3 dB", "-6dB", "2,5 db" -> whole dB (not clamped); empty = 0; null when it is not a number.</summary>
    public static int? ParseDb(string? text)
    {
        string t = (text ?? "").Trim();
        if (t.EndsWith("db", StringComparison.OrdinalIgnoreCase)) t = t[..^2];
        t = t.Trim().Replace(',', '.');
        if (t.Length == 0) return 0;
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
               && !double.IsNaN(v) && !double.IsInfinity(v)
            ? (int)Math.Round(Math.Max(-1e6, Math.Min(1e6, v)), MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>+3 dB / -6 dB / 0 dB.</summary>
    public static string FormatDb(int v) => v > 0 ? $"+{I(v)} dB" : $"{I(v)} dB";

    /// <summary>The filter for these values, or null when the sound is unchanged. A limiter follows any boost so
    /// louder settings do not clip (its auto level is off, so it only catches peaks).</summary>
    public static string? Filter(IReadOnlyList<int> bands, int volumeDb)
    {
        var g = Bands.Select((_, i) => i < bands.Count ? ClampBand(bands[i]) : 0).ToArray();
        int vol = ClampVolume(volumeDb);
        var parts = new List<string>();
        for (int i = 0; i < Bands.Length; i++)
            if (g[i] != 0) parts.Add($"equalizer=f={I(Bands[i])}:t=o:w=1.5:g={I(g[i])}");
        if (vol != 0) parts.Add($"volume={I(vol)}dB");
        if (parts.Count == 0) return null;
        if (vol > 0 || g.Any(x => x > 0)) parts.Add("alimiter=limit=0.97:level=0");
        return string.Join(",", parts);
    }
}
