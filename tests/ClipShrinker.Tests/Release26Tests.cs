using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>2.6 export panel changes: fixed-height resolutions, "Original" audio bitrate, renamed track option.</summary>
public class Release26Tests
{
    [Theory]
    [InlineData("1080p", 1440, 0.75)]
    [InlineData("720p", 1080, 2.0 / 3)]
    [InlineData("4K", 1080, 1.0)]          // never upscaled
    [InlineData("1440p", 1440, 1.0)]
    [InlineData("75%", 1080, 0.75)]        // old projects / fixtures still work
    public void FixedHeights(string mode, double h, double want) =>
        Assert.Equal(want, Geometry.ChooseScale(mode, h * 16 / 9, h, 60, 8e6), 6);

    [Fact]
    public void ResolutionLists()
    {
        Assert.Equal(["Auto", "100%", "4K", "1440p", "1080p", "720p", "50%"], C.ResSizeMode);
        Assert.Equal(["Original", "4K", "1440p", "1080p", "720p", "50%"], C.ResBitrateMode);
        Assert.Equal(2160, Geometry.TargetHeight("4K"));
        Assert.Null(Geometry.TargetHeight("50%"));
    }

    [Fact]
    public void ResLabelSays4K()
    {
        var clip = new ExportClip { Path = "a.mp4", Start = 0, End = 10, W = 3840, H = 2160, Fps = 60, Audio = true, NAudio = 1, Bitrate = 80e6 };
        var st = new ExportSettings { BitrateMbps = 40, ResMode = "4K" };
        var s = AppState.FromSettings(null);
        s.Suffix = "{res}";
        s.KeepName = true;
        Assert.Equal("a_4K", s.OutStem("a", new NameContext(DateTime.Now, null, [clip], st)));
        Assert.Equal("a_1440p", s.OutStem("a", new NameContext(DateTime.Now, null, [clip], st with { ResMode = "1440p" })));
    }

    [Fact]
    public void OriginalAudioBitrate()
    {
        ExportClip C(double ab) => new() { Path = "a.mp4", AudioBitrate = ab };
        Assert.Equal("192k", Text.ResolveAudioK("192k", [C(320_000)]));         // a fixed choice stays
        Assert.Equal("128k", Text.ResolveAudioK("Original", [C(125_000)]));
        Assert.Equal("160k", Text.ResolveAudioK("Original", [C(96_000), C(160_000)]));
        Assert.Equal("320k", Text.ResolveAudioK("Original", [C(1_411_000)]));     // capped
        Assert.Equal("192k", Text.ResolveAudioK("Original", [C(0)]));             // not known
        Assert.Contains("Original", AppState.AudioRates);
    }

    [Fact]
    public void OldTrackOptionNameStillLoads()
    {
        var s = AppState.FromSettings(new JsonObject { ["atrk"] = "Keep separate tracks" });
        Assert.Equal("Keep as separate tracks", s.Atrk);
        var p = AppState.FromSettings(null);
        p.ApplyProject(new JsonObject { ["atrk"] = "Keep separate tracks" });
        Assert.Equal("Keep as separate tracks", p.Atrk);
        Assert.False(p.ToExportSettings(false, out _)!.MixAudio);
    }
}
