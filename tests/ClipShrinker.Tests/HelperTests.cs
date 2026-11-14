using System.Text.Json;
using ClipShrinker.Core;
using ClipShrinker.Core.Export;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Py;

namespace ClipShrinker.Tests;

/// <summary>helpers.json: small pure functions and the Python-compatibility layer.</summary>
public class HelperTests
{
    static readonly JsonElement H = Fixtures.Load("helpers.json");

    /// <summary>Runs fn on each {args, result | error} entry of a table.</summary>
    static void Table(string name, Func<JsonElement, object?> fn, Func<JsonElement, object?>? expected = null)
    {
        var m = new Mismatches(name);
        foreach (var e in H.GetProperty(name).EnumerateArray())
        {
            var args = e.GetProperty("args");
            string where = $"{name}({args.GetRawText()})";
            if (e.TryGetProperty("error", out var err))
            {
                bool threw = false;
                try { fn(args); } catch { threw = true; }
                m.Check(threw, $"{where}: Python raised {err.GetString()}, C# did not");
                continue;
            }
            m.Run(where, () =>
            {
                var want = (expected ?? DefaultExpected)(e.GetProperty("result"));
                var got = fn(args);
                m.Equal(Show(want), Show(got), where);
            });
        }
        m.AssertNone();
    }

    static object? DefaultExpected(JsonElement r) => r.ValueKind switch
    {
        JsonValueKind.String => r.GetString(),
        JsonValueKind.Number => r.GetDouble(),
        JsonValueKind.Null => null,
        JsonValueKind.True or JsonValueKind.False => r.GetBoolean(),
        JsonValueKind.Array => r.EnumerateArray().Select(DefaultExpected).ToList(),
        JsonValueKind.Object => r.Num(),
        _ => r.GetRawText(),
    };

    /// <summary>Text form for comparing (numbers in round-trip form, lists joined).</summary>
    static string Show(object? v) => v switch
    {
        null => "None",
        double d => double.IsNaN(d) ? "nan" : d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => Show((double)f),
        int i => Show((double)i),
        long l => Show((double)l),
        bool b => b ? "True" : "False",
        string s => "'" + s + "'",
        System.Collections.IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(Show)) + "]",
        System.Runtime.CompilerServices.ITuple t => Show(Enumerable.Range(0, t.Length).Select(i => t[i]).ToList()),
        _ => v.ToString() ?? "",
    };

    [Fact] public void FmtElapsed() => Table("fmt_elapsed", a => Text.FmtElapsed(a[0].Num()));
    [Fact] public void FmtTime() => Table("fmt_time", a => Text.FmtTime(a[0].Num()));
    [Fact] public void ParseClock() => Table("parse_clock", a => Text.ParseClock(a[0].GetString()!));
    [Fact] public void FmtTick() => Table("fmt_tick", a => Text.FmtTick(a[0].Num()));
    [Fact] public void ParseFps() => Table("parse_fps", a => Geometry.ParseFps(a[0].GetString()));
    [Fact] public void Even() => Table("even", a => Geometry.Even(a[0].Num()));
    [Fact] public void CropSize() => Table("crop_size", a => Geometry.CropSize(a[0].Int(), a[1].Int(), a[2].NumOrNull()));
    [Fact] public void ChooseScale() => Table("choose_scale", a => Geometry.ChooseScale(a[0].GetString()!, a[1].Num(), a[2].Num(), a[3].Num(), a[4].Num()));
    [Fact] public void TonemapChain() => Table("tonemap_chain", a => Geometry.TonemapChain(a[0].GetString()!));
    [Fact] public void VideoCodecArgs() => Table("video_codec_args", a => Filters.VideoCodecArgs(a[0].GetString()!, a[1].Num()));
    [Fact] public void AtempoChain() => Table("atempo_chain", a => Filters.AtempoChain(a[0].Num()));
    [Fact] public void ClampSpeed() => Table("clamp_speed", a => Text.ClampSpeed(a[0].Num()));
    [Fact] public void AudKey() => Table("aud_key", a => Text.AudKey(a[0].GetString()!));
    [Fact] public void InterpKey() => Table("interp_key", a => Text.InterpKey(a[0].GetString()!));
    [Fact] public void CodecKey() => Table("codec_key", a => Text.CodecKey(a[0].GetString()!));
    [Fact] public void CodecShort() => Table("codec_short", a => Text.CodecShort(a[0].GetString()!));
    [Fact] public void XfadeMode() => Table("xfade_mode", a => Text.XfadeMode(a[0].Str()));
    [Fact] public void ChordLabel() => Table("chord_label", a => Text.ChordLabel(a[0].GetString()!));
    [Fact] public void CleanSuffix() => Table("clean_suffix", a => Text.CleanSuffix(a[0].Str()));

    [Fact]
    public void FractionLimitDenominator() =>
        Table("fraction_limit_denominator_1001", a => { var (n, d) = PyFraction.LimitDenominator(a[0].Num()); return new List<object> { n, d }; });

    [Fact]
    public void PythonFormat()
    {
        var m = new Mismatches("python_format");
        foreach (var e in H.GetProperty("python_format").EnumerateArray())
        {
            double v = e.GetProperty("value").Num();
            foreach (var (spec, n) in new[] { (".3f", 3), (".4f", 4), (".5f", 5), (".2f", 2), (".0f", 0), (".6f", 6) })
                m.Equal(e.GetProperty(spec).GetString(), Py.F(v, n), $"{v:R} {spec}");
            m.Equal(e.GetProperty("g").GetString(), Py.G(v), $"{v:R} g");
        }
        m.AssertNone();
    }

    [Fact]
    public void ClipNaudio() => Table("clip_naudio", a =>
    {
        var d = a[0];
        return new ExportClip { Path = "x", Audio = d.Opt("audio")?.GetBoolean() ?? false, NAudio = d.Opt("naudio")?.Int() }.AudioTracks;
    });

    [Fact]
    public void OutTracks() => Table("out_tracks", a => GraphBuilder.OutTracks(a[0].EnumerateArray().Select(x => x.Int()), a[1].GetBoolean()));

    [Fact]
    public void ColorTags() => Table("color_tags", a =>
        Encoder.ColorTags(a[0].EnumerateArray().Select(c => new ExportClip
        {
            Path = "x", Trc = c.Opt("trc")?.GetString(), Prim = c.Opt("prim")?.GetString(), Space = c.Opt("space")?.GetString(),
        }).ToList(), a[1].GetBoolean()));

    [Fact]
    public void ClipVfilters() => Table("clip_vfilters", a =>
        Filters.ClipVfilters(a[0].GetProperty("w").Int(), a[0].GetProperty("h").Int(), a[0].Opt("trc")?.GetString(),
                             a[1].Int(), a[2].Int(), a[3].NumOrNull(), a[4].GetBoolean()));
}
