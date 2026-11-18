using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Modules;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>2.7 Sound track module: placing sounds, the export / preview mix, saving and undo.</summary>
[Collection("ModuleFlags")]
public class SoundTrackTests
{
    static TimelineClip Clip(int naudio = 1) => new("C:/v/a.mp4", new MediaInfo(1920, 1080, 60, 30, naudio, 8e6, 0, null, null, null));
    static SoundBlock Snd(double at, double len, double inn = 0, int vol = 0, double fade = 0) =>
        new() { Path = "C:/s/horn.wav", At = at, Len = len, In = inn, FileDur = 10, VolumeDb = vol, Fade = fade };

    [Fact]
    public void PlacedInTheEditedTime()
    {
        var u = Sounds.ToUses([Snd(5, 2)], 2, 20, null);
        Assert.Equal(3, u[0].OutStart, 6);                              // trim start 2 -> plays 3 s into the export
        Assert.Equal(2, u[0].Len, 6);
        // a 2x speed section 2..6 halves the time before the sound: (6-2)/2 = 2 s, then 1 s more at normal speed
        var pieces = Timing.BuildPieces(2, 20, [new SpeedSection(2, 6, 2.0)]);
        Assert.Equal(3, Sounds.ToUses([Snd(7, 2)], 2, 20, pieces)[0].OutStart, 6);
    }

    [Fact]
    public void CutAtTheTrimEnds()
    {
        var u = Sounds.ToUses([Snd(1, 4, inn: 0.5)], 2, 20, null)[0];   // starts before the trim start
        Assert.Equal((0.0, 1.5, 3.0), (u.OutStart, u.In, u.Len));
        var e = Sounds.ToUses([Snd(18, 5)], 2, 20, null)[0];            // runs past the end
        Assert.Equal(2, e.Len, 6);
        Assert.Empty(Sounds.ToUses([Snd(25, 2), Snd(0, 1.5)], 2, 20, null));   // wholly outside
    }

    [Fact]
    public void ChainString()
    {
        var s = new SoundUse("x.wav", 1.25, 0.5, 2, -6, 0.3);
        Assert.Equal("[3:a:0]atrim=start=0.500:duration=2.000,asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo," +
                     "volume=-6dB,afade=t=in:d=0.300,afade=t=out:st=1.700:d=0.300,adelay=1250|1250[s0]", Sounds.Chain(s, 3, 48000, "s0"));
        Assert.DoesNotContain("adelay", Sounds.Chain(s with { OutStart = 0, VolumeDb = 0, Fade = 0 }, 1, 44100, "x"));
    }

    [Fact]
    public void ExportMixesIntoTheFirstTrack()
    {
        var row = Clip(naudio: 2);
        row.Sounds.Add(Snd(5, 2));
        var c = row.ToExport();
        Assert.Single(c.Sounds!);
        var g = GraphBuilder.Build([c], 1280, 720, null, null, 0, 60, EffectSettings.Default);
        Assert.Contains(g.Parts, p => p.StartsWith("[1:a:0]atrim="));        // the sound is input 1 (after the clip)
        Assert.Contains(g.Parts, p => p.Contains("[a0_0s][sn0_0]amix=inputs=2:duration=first:dropout_transition=0:normalize=0,alimiter=limit=0.97:level=0[a0_0]"));
        Assert.DoesNotContain(g.Parts, p => p.Contains("[a0_1s]"));          // the microphone track stays clean
        Assert.Equal(["a0_0", "a0_1"], g.AudioLabels!);
    }

    [Fact]
    public void SilentClipGetsATrack()
    {
        var row = Clip(naudio: 0);
        row.Sounds.Add(Snd(1, 2));
        var c = row.ToExport();
        Assert.Equal(1, GraphBuilder.OutTracks([c]));
        var g = GraphBuilder.Build([c], 1280, 720, null, null, 0, 60, EffectSettings.Default);
        Assert.Contains(g.Parts, p => p.StartsWith("anullsrc") && p.EndsWith("[a0_0s]"));
    }

    [Fact]
    public void SoundInputsComeAfterAllClips()
    {
        var a = Clip(); a.Sounds.Add(Snd(1, 1));
        var b = Clip(); b.Sounds.Add(Snd(2, 1));
        var g = GraphBuilder.Build([a.ToExport(), b.ToExport()], 1280, 720, null, null, 0, 60, EffectSettings.Default);
        Assert.Contains(g.Parts, p => p.StartsWith("[2:a:0]") && p.EndsWith("[sn0_0]"));
        Assert.Contains(g.Parts, p => p.StartsWith("[3:a:0]") && p.EndsWith("[sn1_0]"));
    }

    [Fact]
    public void NoSoundsNoChange()
    {
        var row = Clip();
        Assert.Null(row.ToExport().Sounds);
        row.Sounds.Add(Snd(1, 1));
        try
        {
            Features.SoundTrack = false;
            Assert.Null(row.ToExport().Sounds);                            // module off: kept on the clip, left out
        }
        finally { Features.SoundTrack = true; }
    }

    [Fact]
    public void PreviewMixesSounds()
    {
        var fx = EffectSettings.Default with { SoundPlay = [Snd(6, 2)] };
        var cmd = PreviewCommands.PreviewAudio("a.mp4", 5, 4, null, true, 1, null, fx);
        Assert.Equal(["-i", "a.mp4", "-i", "C:/s/horn.wav", "-vn"], cmd.Skip(cmd.IndexOf("-i")).Take(5));
        Assert.Equal("[asn]", cmd[cmd.IndexOf("-map") + 1]);
        Assert.Contains("adelay=1000|1000", cmd[cmd.IndexOf("-filter_complex") + 1]);
        var silent = PreviewCommands.PreviewAudio("a.mp4", 5, 4, null, true, 0, null, fx);
        Assert.StartsWith("anullsrc", silent[silent.IndexOf("-filter_complex") + 1]);
    }

    [Fact]
    public void SavedLoadedAndUndone()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cs_snd_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string video = Path.Combine(dir, "a.mp4"), sound = Path.Combine(dir, "horn.wav"), proj = Path.Combine(dir, "p.clipproj");
            File.WriteAllText(video, ""); File.WriteAllText(sound, "");
            var row = new TimelineClip(video, new MediaInfo(1920, 1080, 60, 30, 1, 8e6, 0, null, null, null));
            row.Sounds.Add(new SoundBlock { Path = sound, At = 3, In = 0.5, Len = 2, FileDur = 4, VolumeDb = -4, Fade = 0.5 });
            var data = ProjectSave.Data([row], 0, AppState.FromSettings(null), proj);
            Assert.Equal("horn.wav", (string)data["clips"]![0]!["sounds"]![0]!["rel"]!);
            File.WriteAllText(proj, data.ToJsonString());
            var load = ProjectFile.Load(proj, _ => row.Info, _ => null);
            var b = Assert.Single(load.Clips[0].Sounds);
            Assert.Equal((3.0, 0.5, 2.0, 4.0, -4, 0.5), (b.At, b.In, b.Len, b.FileDur, b.VolumeDb, b.Fade));

            File.Delete(sound);                                            // a missing sound is listed and skipped
            load = ProjectFile.Load(proj, _ => row.Info, _ => null);
            Assert.Empty(load.Clips[0].Sounds);
            Assert.Contains("horn.wav (sound)", load.Missing);

            var before = ClipState.Capture(row);
            row.Sounds[0].At = 9;
            Assert.NotEqual(before.Signature, ClipState.Capture(row).Signature);
            before.Apply(row);
            Assert.Equal(3, row.Sounds[0].At);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ProjectsWithoutSoundsLookTheSame()
    {
        var data = ProjectSave.Data([Clip()], 0, AppState.FromSettings(null), null);
        Assert.False(((JsonObject)data["clips"]![0]!).ContainsKey("sounds"));
    }

    [Fact]
    public void SoundFilesAndModule()
    {
        Assert.True(Sounds.IsSoundFile("x.MP3"));
        Assert.False(Sounds.IsSoundFile("x.mp4"));
        Assert.Contains(ModuleIds.SoundTrack, ModuleIds.Builtin);
        Assert.Equal("a", C.KeyDefaults["sound"]);
    }
}
