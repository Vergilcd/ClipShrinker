using System.Text.Json;
using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Export;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Settings;

namespace ClipShrinker.Tests;

/// <summary>projects.json (tools/dump_project_fixtures.py): .clipproj loading and the app's settings rules.</summary>
public class ProjectTests
{
    static readonly JsonElement P = Fixtures.Load("projects.json");

    static MediaInfo FakeInfo()
    {
        var i = P.GetProperty("fake_info");
        return new MediaInfo(i.GetProperty("w").Int(), i.GetProperty("h").Int(), i.GetProperty("fps").Num(), i.GetProperty("dur").Num(),
                             i.GetProperty("naudio").Int(), i.GetProperty("bitrate").Num(), (long)i.GetProperty("size").Num(),
                             i.GetProperty("trc").Str(), i.GetProperty("prim").Str(), i.GetProperty("space").Str());
    }

    static bool FakeExists(string p) =>
        p.EndsWith(C.ProjectExt, StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        (p != "" && !p.Contains("missing", StringComparison.OrdinalIgnoreCase));

    /// <summary>The Python state dict next to the C# AppState fields.</summary>
    static IEnumerable<(string Key, string Want, string Got)> StateDiff(JsonElement want, AppState a)
    {
        var got = new Dictionary<string, object>
        {
            ["hdr"] = a.Hdr, ["fast_audio"] = a.FastAudio, ["slow_audio"] = a.SlowAudio, ["fast_vol"] = a.FastVol,
            ["slow_vol"] = a.SlowVol, ["zoom_fx"] = a.ZoomFx, ["zoom_snd"] = a.ZoomSnd, ["zoom_len"] = a.ZoomLen,
            ["zoom_vol"] = a.ZoomVol, ["slow_pitch"] = a.SlowPitch, ["fast_pitch"] = a.FastPitch, ["vhs"] = a.Vhs,
            ["slow_interp"] = a.SlowInterp, ["atrk"] = a.Atrk, ["mute_mic"] = a.MuteMic, ["size"] = a.Size, ["crop"] = a.Crop,
            ["res"] = a.Res, ["codec"] = a.Codec, ["bitrate"] = a.Bitrate, ["res_before"] = a.ResBeforeBitrate,
            ["audio"] = a.Audio, ["mode"] = a.Mode, ["trans"] = a.Trans, ["tlen"] = a.Tlen, ["prefix"] = a.Prefix, ["suffix"] = a.Suffix,
        };
        foreach (var p in want.EnumerateObject())
        {
            string w = p.Value.ValueKind switch { JsonValueKind.String => p.Value.GetString()!, _ => p.Value.GetRawText().ToLowerInvariant() };
            string g = got.TryGetValue(p.Name, out var v) ? (v is bool b ? (b ? "true" : "false") : v.ToString()!) : "<missing>";
            yield return (p.Name, w, g);
        }
    }

    [Fact]
    public void StartupDefaults()
    {
        var m = new Mismatches("app defaults");
        foreach (var (k, w, g) in StateDiff(P.GetProperty("defaults_no_settings"), AppState.FromSettings(null)))
            m.Equal(w, g, "no settings.json: " + k);
        var s = SettingsStore.Load(Path.Combine(Fixtures.FilesDir, "settings.json"));
        foreach (var (k, w, g) in StateDiff(P.GetProperty("defaults_from_settings"), AppState.FromSettings(s)))
            m.Equal(w, g, "settings.json: " + k);
        m.AssertNone();
    }

    [Fact]
    public void LoadProjects()
    {
        var m = new Mismatches("project loading");
        var presets = PresetStore.Load(Path.Combine(Fixtures.FilesDir, "presets.json"));
        var settings = SettingsStore.Load(Path.Combine(Fixtures.FilesDir, "settings.json"));
        foreach (var pr in P.GetProperty("projects").EnumerateArray())
        {
            string file = pr.GetProperty("file").GetString()!;
            var load = ProjectFile.Load(Path.Combine(Fixtures.FilesDir, file), _ => FakeInfo(),
                                        n => presets.FirstOrDefault(p => p.Name == n), FakeExists);
            var rows = pr.GetProperty("rows").EnumerateArray().ToList();
            m.Equal(rows.Count, load.Clips.Count, file + ": clip count");
            for (int i = 0; i < Math.Min(rows.Count, load.Clips.Count); i++)
            {
                var w = rows[i];
                var g = load.Clips[i];
                string where = $"{file} clip {i}";
                m.Equal(w.GetProperty("path").GetString(), g.Path, where + " path");
                m.Equal(w.GetProperty("start").Num(), g.Start, where + " start");
                m.Equal(w.GetProperty("end").Num(), g.End, where + " end");
                m.Equal(w.GetProperty("preset").Str(), g.Preset, where + " preset");
                m.Equal(string.Join(" ", w.GetProperty("sections").EnumerateArray().Select(s =>
                            new SpeedSection(s.GetProperty("a").Num(), s.GetProperty("b").Num(), s.GetProperty("speed").Num()))),
                        string.Join(" ", g.Sections), where + " sections");
                m.Equal(string.Join(" ", w.GetProperty("zooms").EnumerateArray().Select(z =>
                            new ZoomSection(z.GetProperty("a").Num(), z.GetProperty("b").Num(), z.GetProperty("x").Num(),
                                            z.GetProperty("y").Num(), z.GetProperty("w").Num()))),
                        string.Join(" ", g.Zooms), where + " zooms");
                var wc = pr.GetProperty("clips")[i].Clip();
                var gc = g.ToExport();
                m.Equal(wc.Pieces is null ? "None" : string.Join(" ", wc.Pieces), gc.Pieces is null ? "None" : string.Join(" ", gc.Pieces), where + " pieces");
                m.Equal(string.Join(" ", wc.Zooms), string.Join(" ", gc.Zooms), where + " export zooms");
            }
            var state = AppState.FromSettings(settings);
            state.ApplyProject(load.Settings);
            // (2.6 renamed "Keep separate tracks" to "Keep as separate tracks"; 1.0.8 fixtures still use the old wording)
            foreach (var (k, w, gs) in StateDiff(pr.GetProperty("state"), state))
                m.Equal(k == "atrk" && w == "Keep separate tracks" ? "Keep as separate tracks" : w, gs, $"{file}: state {k}");
        }
        m.AssertNone();
    }

    [Fact]
    public void InvalidProjectFile() =>
        Assert.Throws<ProjectException>(() => ProjectFile.Load(Path.Combine(Fixtures.FilesDir, "presets_broken.json"),
                                                               _ => FakeInfo(), _ => null));
}

/// <summary>Real ffmpeg / ffprobe on the generated clips in tests/media (tools/make_test_clips.py).
/// These pass without checking anything when the clips or tools are not there.</summary>
public class MediaIntegrationTests
{
    static readonly string Media = Path.Combine(Fixtures.FixtureDir, "..", "media");
    static readonly string Lib = Path.GetFullPath(Path.Combine(Fixtures.FixtureDir, "..", "..", "..", "Dev", "library"));
    static Tools? T => File.Exists(Path.Combine(Lib, "ffprobe.exe")) && Directory.Exists(Media) ? Tools.Locate(Lib) : null;

    [Fact]
    public void ProbeTestClips()
    {
        if (T is not { } t) return;
        var a = t.Probe(Path.Combine(Media, "a_1080p60.mp4"));
        Assert.Equal((1920, 1080, 60.0, 1, (string?)null), (a.W, a.H, a.Fps, a.NAudio, a.Trc));
        Assert.Equal(20.0, a.Dur, 3);
        var b = t.Probe(Path.Combine(Media, "b_ultrawide_2tracks.mov"));
        Assert.Equal((3440, 1440, 30.0, 2), (b.W, b.H, b.Fps, b.NAudio));
        var c = t.Probe(Path.Combine(Media, "c_720p_ntsc_noaudio.mp4"));
        Assert.Equal((30000.0 / 1001, 0, false), (c.Fps, c.NAudio, c.Audio));
        var d = t.Probe(Path.Combine(Media, "d_hdr10.mp4"));
        Assert.Equal(("smpte2084", "bt2020", "bt2020nc"), (d.Trc, d.Prim, d.Space));
        Assert.True(t.HasNvenc());
        Assert.Throws<MediaException>(() => t.Probe(Path.Combine(Fixtures.FilesDir, "settings.json")));
    }

    [Fact]
    public void CancelStopsTheExportAndCleansUp()
    {
        if (T is not { } t) return;
        string outDir = Directory.CreateTempSubdirectory("clipshrinker_cancel_").FullName;
        try
        {
            string output = Path.Combine(outDir, "x.mp4");
            var statuses = new List<string>();
            var runner = new ProcessRunner(status: s => { lock (statuses) statuses.Add(s); });   // percentages come from here
            var enc = new Encoder(runner, new RealFileSystem(), status: s => { lock (statuses) statuses.Add(s); }) { Ffmpeg = t.Ffmpeg! };
            var info = t.Probe(Path.Combine(Media, "b_ultrawide_2tracks.mov"));
            var clip = ExportClip.FromTimeline(Path.Combine(Media, "b_ultrawide_2tracks.mov"), 0, info.Dur, info,
                                               [new SpeedSection(2, 6, 0.25)], []);
            var task = Task.Run(() => enc.Encode(output, [clip], new ExportSettings { BitrateMbps = 20, ResMode = "Original", Vhs = true }));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(20) && !statuses.Any(s => s.EndsWith('%'))) Thread.Sleep(50);
            Assert.True(statuses.Any(s => s.EndsWith('%')), "no progress was reported: " + string.Join(" | ", statuses));
            enc.Cancel();
            var ex = Assert.ThrowsAny<Exception>(() => task.GetAwaiter().GetResult());
            Assert.IsType<ExportCancelledException>(ex);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
            Assert.Empty(Directory.GetFiles(outDir));          // the .part file was removed
        }
        finally { Directory.Delete(outDir, true); }
    }
}
