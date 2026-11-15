namespace ClipShrinker.Core;

/// <summary>A stretch of source time [A, B] played at Speed.</summary>
public readonly record struct Piece(double A, double B, double Speed);

/// <summary>A speed section as the timeline stores it (source seconds). Mutable and compared by identity:
/// the timeline drags and selects a particular section, like the dicts of the reference.</summary>
public sealed class SpeedSection(double a, double b, double speed)
{
    public double A { get; set; } = a;
    public double B { get; set; } = b;
    public double Speed { get; set; } = speed;
    public override string ToString() => FormattableString.Invariant($"SpeedSection {{ A = {A:R}, B = {B:R}, Speed = {Speed:R} }}");
}

/// <summary>A zoom section as the timeline stores it: source seconds, area as fractions of the cropped picture.
/// Mutable and compared by identity, like SpeedSection.</summary>
public sealed class ZoomSection(double a, double b, double x, double y, double w)
{
    public double A { get; set; } = a;
    public double B { get; set; } = b;
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double W { get; set; } = w;
    public override string ToString() => FormattableString.Invariant($"ZoomSection {{ A = {A:R}, B = {B:R}, X = {X:R}, Y = {Y:R}, W = {W:R} }}");
}

/// <summary>A zoom clipped to the trim: (a, b, x, y, w) as in ClipRow.clip()["zooms"].</summary>
public readonly record struct Zoom(double A, double B, double X, double Y, double W);

/// <summary>A zoom window in output time and output-frame fractions.</summary>
public readonly record struct ZoomSpan(double OutStart, double OutEnd, double Left, double Top, double Width);

/// <summary>What ffprobe found out about a file (reference probe()).</summary>
public sealed record MediaInfo(int W, int H, double Fps, double Dur, int NAudio, double Bitrate, long Size,
                               string? Trc, string? Prim, string? Space)
{
    public bool Audio => NAudio > 0;
}

/// <summary>One clip as the encoder gets it (reference ClipRow.clip()).</summary>
public sealed record ExportClip
{
    public required string Path { get; init; }
    public double Start { get; init; }
    public double End { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    public double Fps { get; init; }
    public bool Audio { get; init; }
    /// <summary>null = not known (then Audio decides: 1 or 0), like c.get("naudio", ...).</summary>
    public int? NAudio { get; init; }
    public double Bitrate { get; init; }
    public string? Trc { get; init; }
    public string? Prim { get; init; }
    public string? Space { get; init; }
    /// <summary>null = no speed sections.</summary>
    public IReadOnlyList<Piece>? Pieces { get; init; }
    public IReadOnlyList<Zoom> Zooms { get; init; } = [];
    /// <summary>Extra video filters from plugins (Picture adjustments), after crop / scale / tone-mapping. null = none.</summary>
    public IReadOnlyList<string>? PluginVf { get; init; }

    /// <summary>clip_naudio(c).</summary>
    public int AudioTracks => NAudio ?? (Audio ? 1 : 0);

    /// <summary>get_pieces(c): the speed pieces, or the whole trim at 1x.</summary>
    public IReadOnlyList<Piece> GetPieces() =>
        Pieces is { Count: > 0 } ? Pieces : [new Piece(Start, End, 1.0)];

    /// <summary>ClipRow.clip(): builds the export clip from timeline data (visible sections and zooms only).</summary>
    public static ExportClip FromTimeline(string path, double start, double end, MediaInfo info,
                                          IEnumerable<SpeedSection> sections, IEnumerable<ZoomSection> zooms)
    {
        var vis = sections.OrderBy(s => s.A).Where(s => Py.Py.Min(s.B, end) - Py.Py.Max(s.A, start) >= 0.05).ToList();
        var zs = new List<Zoom>();
        foreach (var z in zooms.OrderBy(z => z.A))
        {
            double a = Py.Py.Max(z.A, start), b = Py.Py.Min(z.B, end);
            if (b - a >= 0.05) zs.Add(new Zoom(a, b, z.X, z.Y, z.W));
        }
        return new ExportClip
        {
            Path = path, Start = start, End = end, W = info.W, H = info.H, Fps = info.Fps, Audio = info.Audio,
            NAudio = info.NAudio, Bitrate = info.Bitrate, Trc = info.Trc, Prim = info.Prim, Space = info.Space,
            Pieces = vis.Count > 0 ? Timing.BuildPieces(start, end, vis) : null, Zooms = zs,
        };
    }
}

/// <summary>
/// The effect settings the filter builders read. In 1.0.8 these were the module globals AUD, VHS, SLOW, ZOOM.
/// </summary>
public sealed record EffectSettings
{
    /// <summary>Sound of sped-up sections: fx | normal | mute.</summary>
    public string Fast { get; init; } = "fx";
    /// <summary>Sound of slow-motion sections: fx | normal | mute.</summary>
    public string Slow { get; init; } = "fx";
    /// <summary>Slow motion: the pitch drops with the speed.</summary>
    public bool Deep { get; init; } = true;
    /// <summary>Speed-up: the pitch rises with the speed.</summary>
    public bool High { get; init; } = false;
    public int FastVol { get; init; } = 70;
    public int SlowVol { get; init; } = 150;
    public bool VhsOn { get; init; } = false;
    public bool SlowBlend { get; init; } = true;
    public bool SlowRife { get; init; } = false;
    public bool ZoomMotion { get; init; } = true;
    public double ZoomLen { get; init; } = 0.4;
    public bool ZoomSound { get; init; } = true;
    public int ZoomVol { get; init; } = 80;
    /// <summary>Zooms of the clip being previewed (ZOOM["play"]).</summary>
    public IReadOnlyList<Zoom> ZoomPlay { get; init; } = [];
    /// <summary>The preview's Picture adjustments filter (null = none). Exports take it from ExportClip.PluginVf.</summary>
    public string? PictureVf { get; init; }

    public static EffectSettings Default { get; } = new();

    /// <summary>Same mapping as Encoder._encode (reference lines 1638-1651).</summary>
    public static EffectSettings FromExport(ExportSettings st)
    {
        string mode = Text.InterpKey(st.SlowInterp);
        return new EffectSettings
        {
            VhsOn = st.Vhs,
            Fast = Text.AudKey(st.FastAudio),
            Slow = Text.AudKey(st.SlowAudio),
            Deep = st.SlowPitch == C.SlowPitch[0],
            High = st.FastPitch == C.FastPitch[0],
            FastVol = st.FastVol,
            SlowVol = st.SlowVol,
            ZoomMotion = st.ZoomFx == C.ZoomFx[0],
            ZoomLen = Py.Py.ParseFloat(st.ZoomLen) ?? throw new FormatException($"could not convert string to float: '{st.ZoomLen}'"),
            ZoomSound = st.ZoomSnd == C.ZoomAudio[0],
            ZoomVol = st.ZoomVol,
            SlowBlend = mode != "repeat",
            SlowRife = mode == "rife",
        };
    }
}

/// <summary>Export settings (the reference "st" dict). Defaults are the ones st.get(...) falls back to.</summary>
public sealed record ExportSettings
{
    public double? TargetMb { get; init; }
    public double? BitrateMbps { get; init; }
    public string ResMode { get; init; } = "Auto";
    /// <summary>ffmpeg encoder name: h264_nvenc | hevc_nvenc.</summary>
    public string Codec { get; init; } = "hevc_nvenc";
    public string AudioK { get; init; } = "128k";
    public double? Ratio { get; init; }
    public string? Xfade { get; init; }
    public double T { get; init; } = 0.4;
    public bool Tonemap { get; init; }
    public bool MuteFast { get; init; } = true;
    public bool MixAudio { get; init; }
    public bool Vhs { get; init; }
    public string FastAudio { get; init; } = C.FastAudio[0];
    public string SlowAudio { get; init; } = C.SlowAudio[0];
    public string SlowPitch { get; init; } = C.SlowPitch[0];
    public string FastPitch { get; init; } = C.FastPitch[1];
    public int FastVol { get; init; } = 70;
    public int SlowVol { get; init; } = 150;
    public string ZoomFx { get; init; } = C.ZoomFx[0];
    public string ZoomLen { get; init; } = "0.4";
    public string ZoomSnd { get; init; } = C.ZoomAudio[0];
    public int ZoomVol { get; init; } = 80;
    public string SlowInterp { get; init; } = C.SlowInterp[1];
}
