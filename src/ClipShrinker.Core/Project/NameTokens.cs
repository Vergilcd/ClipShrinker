using System.Globalization;
using System.Text.RegularExpressions;
using ClipShrinker.Core.Ffmpeg;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Project;

/// <summary>What a file name's fields are filled in from: the export time, the clip's bitrate preset (null = none)
/// and, when known, the clips and settings of that file (for the output resolution).</summary>
public sealed record NameContext(DateTime Now, string? Preset = null, IReadOnlyList<ExportClip>? Clips = null, ExportSettings? Settings = null);

/// <summary>2.1: fields in the Prefix / Suffix boxes that are replaced when a file is named (case does not matter;
/// an unknown {word} stays as typed).</summary>
public static partial class NameTokens
{
    /// <summary>(field, button text, explanation) in the order the buttons are shown.</summary>
    public static readonly IReadOnlyList<(string Token, string Label, string Help)> All =
    [
        ("{date}", "Date", "export date, e.g. 2026-11-15"),
        ("{time}", "Time", "export time, e.g. 14-30"),
        ("{preset}", "Preset", "the clip's bitrate preset (Auto when none)"),
        ("{res}", "Resolution", "output height, e.g. 1080p"),
        ("{size}", "Max size", "the size limit, e.g. 50MB"),
        ("{codec}", "Codec", "HEVC or H264"),
    ];

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex TokenRx();

    public static string Expand(string text, AppState s, NameContext ctx)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text ?? "";
        return TokenRx().Replace(text, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "date" => ctx.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "time" => ctx.Now.ToString("HH-mm", CultureInfo.InvariantCulture),
            "preset" => ctx.Preset is { Length: > 0 } p ? p : "Auto",
            "res" => Resolution(s, ctx),
            "size" => (s.Size.Trim() is { Length: > 0 } z ? z.Replace(",", ".") : "?") + "MB",
            "codec" => s.CodecTag,
            _ => m.Value,
        });
    }

    /// <summary>The output height ("1080p"), worked out like the encoder's first pass (Geometry.ChooseScale with the
    /// first bitrate it tries). Without clips: the Resolution setting.</summary>
    static string Resolution(AppState s, NameContext ctx)
    {
        if (ctx.Clips is not { Count: > 0 } clips || ctx.Settings is not { } st) return s.Res;
        var sizes = clips.Select(c => Geometry.CropSize(c.W, c.H, st.Ratio)).ToList();
        var (refw, refh) = sizes.Aggregate((best, z) => (long)z.W * z.H > (long)best.W * best.H ? z : best);
        double fps = clips.Select(c => c.Fps).Aggregate(Max);
        double v;
        if (st.BitrateMbps is { } b && b != 0) v = b * 1_000_000;
        else
        {
            double total = Sum(clips.Select(c => Timing.Duration(c.GetPieces())));
            if (clips.Count > 1 && !string.IsNullOrEmpty(st.Xfade)) total -= (clips.Count - 1) * st.T;
            long audioBps = long.Parse(st.AudioK.TrimEnd('k'), CultureInfo.InvariantCulture) * 1000 * GraphBuilder.OutTracks(clips, st.MixAudio);
            v = (st.TargetMb ?? 0) * 1024 * 1024 * 0.96 * 8 / Max(total, 0.1) - audioBps;
            var rates = clips.Select(c => c.Bitrate).ToList();
            if (rates.All(r => r > 0) && v > rates.Aggregate(Max)) v = rates.Aggregate(Max);
            v = Max(v, 20_000);
        }
        double scale = Geometry.ChooseScale(st.ResMode, refw, refh, fps, v);
        return $"{Geometry.Even(refh * scale)}p";
    }
}
