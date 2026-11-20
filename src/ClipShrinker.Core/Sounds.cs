using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core;

/// <summary>2.7 Sound track module: one sound file placed on a clip's timeline. At is the clip (source) time where it
/// starts; In / Len pick the part of the sound file that plays (seconds, real time: speed sections do not change it).</summary>
public sealed class SoundBlock
{
    public required string Path { get; init; }
    public double At { get; set; }
    public double In { get; set; }
    public double Len { get; set; }
    /// <summary>Length of the whole sound file (trimming never goes past it).</summary>
    public double FileDur { get; init; }
    /// <summary>-20 .. +20 dB.</summary>
    public int VolumeDb { get; set; }
    /// <summary>Fade in and out, seconds (0 = none; at most half the length).</summary>
    public double Fade { get; set; }

    public string Name => System.IO.Path.GetFileName(Path);
    /// <summary>At + Len: where the sound ends only while no speed section or pause lies under it. The timeline and
    /// the agent use TimelineClip.SoundEnd (2.9), which follows them.</summary>
    public double End => At + Len;

    public SoundBlock Clone() => new() { Path = Path, At = At, In = In, Len = Len, FileDur = FileDur, VolumeDb = VolumeDb, Fade = Fade };
}

/// <summary>One sound as the export gets it: where it starts in the clip's edited (output) time and which part plays.</summary>
public sealed record SoundUse(string Path, double OutStart, double In, double Len, int VolumeDb, double Fade);

public static class Sounds
{
    public static readonly string[] Exts = [".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".opus"];
    public const double MinLen = 0.1;
    public const int VolumeMax = 20;

    public static bool IsSoundFile(string path) => Exts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static int ClampVolume(int v) => Math.Max(-VolumeMax, Math.Min(VolumeMax, v));
    public static double ClampFade(double f, double len) => Math.Max(0, Math.Min(f, Math.Min(5, len / 2)));

    /// <summary>2.9: where a sound ends in clip (source) time. It plays Len real seconds from At, so a slow-motion
    /// section under it makes it end sooner in source time, a speed-up later, a pause sooner.</summary>
    public static double SrcEnd(SoundBlock b, IReadOnlyList<Piece> pieces) =>
        Timing.OutToSrcX(pieces, Timing.SrcToOutX(pieces, b.At) + b.Len);

    /// <summary>2.9: the real length a sound gets when its end is dragged to source time endSrc.</summary>
    public static double LenTo(double at, double endSrc, IReadOnlyList<Piece> pieces) =>
        Timing.SrcToOutX(pieces, endSrc) - Timing.SrcToOutX(pieces, at);

    /// <summary>The blocks of a clip trimmed to start..end and placed in its edited time (pieces = its speed sections).
    /// A sound that starts before the trim start plays from the matching point; one that runs past the end is cut.</summary>
    public static List<SoundUse> ToUses(IEnumerable<SoundBlock> blocks, double start, double end, IReadOnlyList<Piece>? pieces)
    {
        IReadOnlyList<Piece> pcs = pieces is { Count: > 0 } ? pieces : [new Piece(start, end, 1.0)];
        double outDur = Timing.Duration(pcs);
        var res = new List<SoundUse>();
        foreach (var b in blocks.OrderBy(b => b.At))
        {
            if (b.At >= end - 0.01 || b.End <= start + 0.01) continue;
            double skip = Math.Max(0, start - b.At);
            double outStart = Timing.SrcToOut(pcs, Math.Max(b.At, start));
            double len = Math.Min(b.Len - skip, outDur - outStart);
            if (len < 0.05) continue;
            res.Add(new SoundUse(b.Path, outStart, b.In + skip, len, ClampVolume(b.VolumeDb), ClampFade(b.Fade, len)));
        }
        return res;
    }

    /// <summary>Filter for one sound input: its part, stereo at the given rate, volume, fades, then delayed to its start.</summary>
    public static string Chain(SoundUse s, int input, int rate, string outLabel)
    {
        var f = new List<string>
        {
            $"atrim=start={F(s.In, 3)}:duration={F(s.Len, 3)}", "asetpts=PTS-STARTPTS",
            $"aresample={I(rate)}", "aformat=sample_fmts=fltp:channel_layouts=stereo",
        };
        if (s.VolumeDb != 0) f.Add($"volume={I(s.VolumeDb)}dB");
        if (s.Fade >= 0.01)
        {
            f.Add($"afade=t=in:d={F(s.Fade, 3)}");
            f.Add($"afade=t=out:st={F(Math.Max(0, s.Len - s.Fade), 3)}:d={F(s.Fade, 3)}");
        }
        long ms = Math.Max(0, (long)Math.Round(s.OutStart * 1000));
        if (ms > 0) f.Add($"adelay={I(ms)}|{I(ms)}");
        return $"[{I(input)}:a:0]" + string.Join(",", f) + $"[{outLabel}]";
    }

    /// <summary>Mixes the sounds into one audio label: [src] + sounds -> [dst], as long as src (sounds are cut at its end).</summary>
    public static List<string> MixParts(string src, string dst, IReadOnlyList<SoundUse> uses, int firstInput, int rate, string tag)
    {
        var parts = new List<string>();
        var labs = new List<string>();
        for (int k = 0; k < uses.Count; k++)
        {
            string l = $"{tag}{I(k)}";
            parts.Add(Chain(uses[k], firstInput + k, rate, l));
            labs.Add($"[{l}]");
        }
        parts.Add($"[{src}]" + string.Concat(labs) + $"amix=inputs={I(uses.Count + 1)}:duration=first:dropout_transition=0:normalize=0," +
                  $"alimiter=limit=0.97:level=0[{dst}]");      // (the limiter keeps loud sounds over loud game audio from clipping)
        return parts;
    }
}
