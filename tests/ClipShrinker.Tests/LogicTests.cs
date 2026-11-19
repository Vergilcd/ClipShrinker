using System.Text.Json;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Settings;

namespace ClipShrinker.Tests;

/// <summary>pieces.json: speed pieces, time mapping, zoom timing.</summary>
public class PiecesTests
{
    static readonly JsonElement P = Fixtures.Load("pieces.json");

    [Fact]
    public void BuildPiecesAndTimeMapping()
    {
        var m = new Mismatches("build_pieces");
        foreach (var e in P.GetProperty("build_pieces").EnumerateArray())
        {
            string name = e.GetProperty("name").GetString()!;
            var secs = e.GetProperty("sections").EnumerateArray()
                .Select(s => new SpeedSection(s.GetProperty("a").Num(), s.GetProperty("b").Num(), s.GetProperty("speed").Num())).ToList();
            var pieces = Timing.BuildPieces(e.GetProperty("start").Num(), e.GetProperty("end").Num(), secs);
            m.Equal(string.Join(" ", e.GetProperty("pieces").Pieces()!), string.Join(" ", pieces), $"{name}: pieces");
            m.Equal(e.GetProperty("duration").Num(), Timing.Duration(pieces), $"{name}: duration");
            m.Equal(e.GetProperty("is_plain").GetBoolean(), Timing.IsPlain(pieces), $"{name}: is_plain");
            foreach (var p in e.GetProperty("out_to_src").EnumerateArray())
                m.Equal(p[1].Num(), Timing.OutToSrc(pieces, p[0].Num()), $"{name}: out_to_src({p[0].Num():R})");
            foreach (var p in e.GetProperty("src_to_out").EnumerateArray())
                m.Equal(p[1].Num(), Timing.SrcToOut(pieces, p[0].Num()), $"{name}: src_to_out({p[0].Num():R})");
        }
        foreach (var e in P.GetProperty("is_plain_extra").EnumerateArray())
            m.Equal(e.GetProperty("result").GetBoolean(), Timing.IsPlain(e.GetProperty("pieces").Pieces()), "is_plain " + e.GetProperty("pieces").GetRawText());
        m.AssertNone();
    }

    [Fact]
    public void ZoomTimesAndSpans()
    {
        var m = new Mismatches("zoom timing");
        foreach (var e in P.GetProperty("zoom_times").EnumerateArray().Concat(P.GetProperty("zoom_times_empty").EnumerateArray()))
        {
            var res = Timing.ZoomTimes(e.GetProperty("zooms").Zooms(), e.GetProperty("pieces").Pieces());
            var want = e.GetProperty("result").EnumerateArray()
                .Select(r => $"{r[0].Num():R} {r[1].Num():R} {string.Join(",", r[2].EnumerateArray().Select(x => x.Num().ToString("R")))}");
            var got = res.Select(r => $"{r.OutStart:R} {r.OutEnd:R} {r.Z.A:R},{r.Z.B:R},{r.Z.X:R},{r.Z.Y:R},{r.Z.W:R}");
            m.Seq(want.ToList(), got.ToList(), "zoom_times " + (e.Opt("name")?.GetString() ?? "empty"));
        }
        foreach (var e in P.GetProperty("zoom_spans").EnumerateArray())
        {
            var res = Timing.ZoomSpans(e.GetProperty("zooms").Zooms(), e.GetProperty("pieces").Pieces()!,
                e.GetProperty("cw0").Num(), e.GetProperty("ch0").Num(), e.GetProperty("W").Num(), e.GetProperty("H").Num());
            var want = e.GetProperty("result").EnumerateArray()
                .Select(r => string.Join(" ", r.EnumerateArray().Select(x => x.Num().ToString("R")))).ToList();
            var got = res.Select(s => $"{s.OutStart:R} {s.OutEnd:R} {s.Left:R} {s.Top:R} {s.Width:R}").ToList();
            m.Seq(want, got, $"zoom_spans {e.GetProperty("name").GetString()} {e.GetProperty("cw0")}x{e.GetProperty("ch0")} -> {e.GetProperty("W")}x{e.GetProperty("H")}");
        }
        m.AssertNone();
    }
}

/// <summary>filters.json: every filter builder on its own.</summary>
public class FilterTests
{
    static readonly JsonElement F = Fixtures.Load("filters.json");

    static void Each(string name, Func<JsonElement, EffectSettings, IReadOnlyList<string>> fn)
    {
        var m = new Mismatches(name);
        foreach (var e in F.GetProperty(name).EnumerateArray())
        {
            var args = e.GetProperty("args");
            var fx = e.Opt("globals")?.Effects() ?? EffectSettings.Default;
            string where = $"{name}({args.GetRawText()})";
            m.Run(where, () =>
            {
                var res = e.GetProperty("result");
                var want = res.ValueKind == JsonValueKind.String ? [res.GetString()!] : res.Strs();
                m.Seq(want, fn(args, fx), where);
            });
        }
        m.AssertNone();
    }

    [Fact]
    public void GlitchParts() => Each("glitch_parts", (a, _) =>
        Filters.GlitchParts(a[0].GetString()!, a[1].GetString()!, a[2].Num(), a[3].Num(), a[4].Int(), a[5].Int(), a[6].GetString()!, a[7].Int()));

    [Fact]
    public void VhsVideoParts() => Each("vhs_video_parts", (a, _) =>
        Filters.VhsVideoParts(a[0].GetString()!, a[1].GetString()!, a[2].Num(), a[3].Size()));

    [Fact]
    public void VhsAudioParts() => Each("vhs_audio_parts", (a, _) =>
        Filters.VhsAudioParts(a[0].GetString()!, a[1].GetString()!, a[2].Num(), a[3].Int()));

    [Fact]
    public void SlowAudioParts() => Each("slow_audio_parts", (a, _) =>
        Filters.SlowAudioParts(a[0].GetString()!, a[1].GetString()!, a[2].Num(), a[3].Int()));

    [Fact]
    public void ZoomVfilter() => Each("zoom_vfilter", (a, fx) =>
        [Filters.ZoomVfilter(a[0].EnumerateArray().Select(s => new ZoomSpan(s[0].Num(), s[1].Num(), s[2].Num(), s[3].Num(), s[4].Num())).ToList(),
                             a[1].Int(), a[2].Int(), fx)]);

    [Fact]
    public void ZoomWhooshParts() => Each("zoom_whoosh_parts", (a, fx) =>
        Filters.ZoomWhooshParts(a[0].GetString()!, a[1].GetString()!, a[2].EnumerateArray().Select(t => (t[0].Num(), t[1].Num())).ToList(),
                                a[3].Int(), a[4].GetString()!, fx));

    [Fact]
    public void SegVideo() => Each("seg_video", (a, fx) =>
        Filters.SegVideo(a[0].GetString()!, a[1].Pieces()!, a[2].Num(), a[3].GetString()!, a[4].GetString()!, a[5].Size(),
                         a[6].NumOrNull(), a[7].RifeMap(), fx));

    [Fact]
    public void SegAudio() => Each("seg_audio", (a, fx) =>
        Filters.SegAudio(a[0].GetString()!, a[1].Pieces()!, a[2].Num(), a[3].GetString()!, a[4].GetString()!, a[5].GetBoolean(), fx, a[6].Int()));
}

/// <summary>graphs.json: the whole export filter graph, and timeline row -> export clip.</summary>
public class GraphTests
{
    static readonly JsonElement G = Fixtures.Load("graphs.json");

    [Fact]
    public void ClipsFromTimelineRows()
    {
        var m = new Mismatches("ClipRow.clip()");
        foreach (var g in G.EnumerateArray())
            foreach (var c in g.GetProperty("clips").EnumerateArray())
            {
                string where = g.GetProperty("name").GetString()!;
                var want = c.GetProperty("clip").Clip();
                var got = c.ClipFromRow();
                m.Equal(want.Pieces is null ? "None" : string.Join(" ", want.Pieces), got.Pieces is null ? "None" : string.Join(" ", got.Pieces), where + ": pieces");
                m.Equal(string.Join(" ", want.Zooms), string.Join(" ", got.Zooms), where + ": zooms");
            }
        m.AssertNone();
    }

    [Fact]
    public void BuildGraph()
    {
        var m = new Mismatches("build_graph");
        foreach (var g in G.EnumerateArray())
        {
            string name = g.GetProperty("name").GetString()!;
            m.Run(name, () =>
            {
                var a = g.GetProperty("args");
                var clips = g.GetProperty("clips").EnumerateArray().Select(c => c.GetProperty("clip").Clip()).ToList();
                var fx = g.GetProperty("globals").Effects();
                var res = GraphBuilder.Build(clips, a.GetProperty("W").Int(), a.GetProperty("H").Int(), a.GetProperty("ratio").NumOrNull(),
                    a.GetProperty("xfade").Str(), a.GetProperty("T").Num(), a.GetProperty("fps").Num(), fx,
                    a.GetProperty("tonemap").GetBoolean(), a.GetProperty("mute_fast").GetBoolean(), a.GetProperty("mix").GetBoolean(),
                    a.GetProperty("rife").RifeMaps());
                var want = g.GetProperty("result");
                m.Seq(want.GetProperty("graph_parts").Strs(), res.Parts, name + ": graph");
                m.Equal(want.GetProperty("graph").GetString(), res.Graph, name + ": graph text");
                m.Equal(want.GetProperty("video_label").GetString(), res.VideoLabel, name + ": video label");
                var al = want.GetProperty("audio_labels");
                m.Equal(al.IsNull() ? "None" : string.Join(",", al.Strs()), res.AudioLabels is null ? "None" : string.Join(",", res.AudioLabels), name + ": audio labels");
            });
        }
        m.AssertNone();
    }
}

/// <summary>preview.json: player, preview audio, thumbnails, effect samples, transition hover preview.</summary>
public class PreviewTests
{
    static readonly JsonElement P = Fixtures.Load("preview.json");

    static void Each(string name, Func<JsonElement, EffectSettings, List<List<string>>> fn, string resultKey = "result")
    {
        var m = new Mismatches(name);
        foreach (var e in P.GetProperty(name).EnumerateArray())
        {
            var args = e.Opt("args") ?? e;
            var fx = e.Opt("globals")?.Effects() ?? EffectSettings.Default;
            string where = $"{name}({args.GetRawText()})";
            m.Run(where, () =>
            {
                var res = e.GetProperty(resultKey);
                var want = resultKey == "result" ? [res.Strs()] : res.EnumerateArray().Select(c => c.Strs()).ToList();
                var got = fn(args, fx);
                m.Equal(want.Count, got.Count, where + ": number of commands");
                for (int i = 0; i < Math.Min(want.Count, got.Count); i++) m.Seq(want[i], got[i], $"{where}: command {i}");
            });
        }
        m.AssertNone();
    }

    [Fact]
    public void PlayCommand() => Each("play_command", (a, fx) =>
        [PreviewCommands.Play(a[0].GetString()!, a[1].Num(), a[2].Num(), a[3].Size(), a[4].Int(), a[5].Int(), a[6].Num(), a[7].Int(),
                              a[8].Str(), a[9].Pieces(), fx)]);

    [Fact]
    public void PreviewAudioCommand() => Each("preview_audio_cmd", (a, fx) =>
        [PreviewCommands.PreviewAudio(a[0].GetString()!, a[1].Num(), a[2].Num(), a[3].Pieces(), a[4].GetBoolean(), a[5].Int(), a[6].Str(), fx)]);

    [Fact]
    public void EffectSampleCommand() => Each("effect_sample_cmd", (a, fx) =>
        [PreviewCommands.EffectSample(a[0].GetString()!, a[1].Str(), a[2].Num(), a[3].GetString()!, fx, a[4].Int())]);

    /// <summary>2.7.5 deviates from 1.0.8 on purpose: HDR thumbnails get an even size before the tone map (1.0.8 failed
    /// on clips like 1920x1088). The fixtures are compared without that suffix; HdrThumbsAreEven checks it.</summary>
    static List<string> WithoutEvenSize(List<string> cmd) => cmd.Select(x => x.Replace(":force_divisible_by=2", "")).ToList();

    [Fact]
    public void GrabFrameCommand() => Each("grab_frame", (a, _) =>
        [WithoutEvenSize(PreviewCommands.GrabFrame(a[0].GetString()!, a[1].Num(), a[2].Int(), a[3].Int(), a[4].Size(), a[5].Str()))], "commands");

    [Fact]
    public void HdrThumbsAreEven()
    {
        Assert.Contains("decrease:force_divisible_by=2,zscale", string.Join(" ", PreviewCommands.GrabFrame("a.webm", 1, 127, 72, null, "smpte2084")));
        Assert.Contains("decrease:force_divisible_by=2,zscale", string.Join(" ", PreviewCommands.GrabFramesBatch("a.webm", [1, 2], 127, 72, "smpte2084")));
        Assert.DoesNotContain("force_divisible_by", string.Join(" ", PreviewCommands.GrabFramesBatch("a.mp4", [1, 2], 127, 72)));
    }

    [Fact]
    public void GrabFramesCommands() => Each("grab_frames", (a, _) =>
    {
        // The fixture made every batch process fail, so each batch is followed by one grab_frame per time.
        var times = a[1].EnumerateArray().Select(t => t.Num()).ToList();
        var cmds = new List<List<string>>();
        for (int b0 = 0; b0 < times.Count; b0 += C.ThumbBatch)
        {
            var batch = times.Skip(b0).Take(C.ThumbBatch).ToList();
            cmds.Add(WithoutEvenSize(PreviewCommands.GrabFramesBatch(a[0].GetString()!, batch, a[2].Int(), a[3].Int(), a[4].Str())));
            cmds.AddRange(batch.Select(t => WithoutEvenSize(PreviewCommands.GrabFrame(a[0].GetString()!, t, a[2].Int(), a[3].Int(), null, a[4].Str()))));
        }
        return cmds;
    }, "commands");

    [Fact]
    public void TransitionPreviewCommand() => Each("transition_preview", (e, _) =>
    {
        var src = e.GetProperty("source_list");
        var sources = src.IsNull() ? null : src.EnumerateArray().Select(s => (s[0].GetString()!, s[1].Num())).ToList();
        return [PreviewCommands.TransitionPreview(e.GetProperty("transition").GetString()!, sources)];
    }, "commands");
}

/// <summary>files.json + files/: presets and settings loading.</summary>
public class StoreTests
{
    static readonly JsonElement Fi = Fixtures.Load("files.json");

    [Fact]
    public void LoadPresets()
    {
        var m = new Mismatches("load_presets");
        foreach (var p in Fi.GetProperty("load_presets").EnumerateObject())
        {
            string path = p.Name == "missing file" ? Path.Combine(Fixtures.FilesDir, "does_not_exist.json") : Path.Combine(Fixtures.FilesDir, p.Name);
            var want = p.Value.EnumerateArray().Select(x => $"{x.GetProperty("name").GetString()}|{x.GetProperty("mbps").Num():R}|{x.GetProperty("key").GetString()}").ToList();
            var got = PresetStore.Load(path).Select(x => $"{x.Name}|{x.Mbps:R}|{x.Key}").ToList();
            m.Seq(want, got, p.Name);
        }
        m.AssertNone();
    }

    [Fact]
    public void LoadSettingsKeepsEveryKey()
    {
        var want = Fi.GetProperty("settings_parsed");
        var got = SettingsStore.Load(Path.Combine(Fixtures.FilesDir, "settings.json"));
        Assert.Equal(want.EnumerateObject().Select(p => p.Name), got.Select(p => p.Key));
        foreach (var p in want.EnumerateObject())
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(p.Value.GetRawText()), got[p.Name]), p.Name);
        Assert.Empty(SettingsStore.Load(Path.Combine(Fixtures.FilesDir, "presets_broken.json")));
    }
}
