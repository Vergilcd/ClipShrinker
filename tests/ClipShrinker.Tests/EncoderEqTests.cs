using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Plugins;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>2.6: encoders for every PC, and the Sound equalizer plugin.</summary>
public class EncoderEqTests
{
    static TimelineClip Clip(string path = "C:/v/a.mp4", double dur = 30) =>
        new(path, new MediaInfo(1920, 1080, 60, dur, 1, 8e6, 0, null, null, null));

    // ------------------------------------------------------------------ encoders
    [Fact]
    public void NvencArgsAreUnchanged()
    {
        Assert.Equal(["-c:v", "hevc_nvenc", "-preset", "p5", "-tune", "hq", "-rc", "vbr", "-b:v", "4000k", "-maxrate", "4000k",
                      "-bufsize", "8000k", "-spatial-aq", "1", "-pix_fmt", "yuv420p", "-tag:v", "hvc1"],
                     Filters.VideoCodecArgs("hevc_nvenc", 4_000_000));
        Assert.Equal(["-c:v", "h264_nvenc", "-preset", "p5", "-tune", "hq", "-rc", "vbr", "-b:v", "20k", "-maxrate", "20k",
                      "-bufsize", "40k", "-spatial-aq", "1", "-pix_fmt", "yuv420p"],
                     Filters.VideoCodecArgs("h264_nvenc", 100));
    }

    [Fact]
    public void OtherEncoderArgs()
    {
        var amf = Filters.VideoCodecArgs("hevc_amf", 3_000_000);
        Assert.Equal(["-c:v", "hevc_amf"], amf.Take(2));
        Assert.Contains("vbr_peak", amf);
        Assert.Equal("hvc1", amf[^1]);
        var qsv = Filters.VideoCodecArgs("h264_qsv", 3_000_000);
        Assert.Equal(["-c:v", "h264_qsv"], qsv.Take(2));
        Assert.Equal("nv12", qsv[qsv.ToList().IndexOf("-pix_fmt") + 1]);
        var mfHw = Filters.VideoCodecArgs("h264_mf_hw", 3_000_000);
        Assert.Equal(["-c:v", "h264_mf"], mfHw.Take(2));
        Assert.Contains("-hw_encoding", mfHw);
        Assert.DoesNotContain("-hw_encoding", Filters.VideoCodecArgs("h264_mf", 3_000_000));
        Assert.Equal("3000k", mfHw[mfHw.ToList().IndexOf("-b:v") + 1]);
    }

    [Fact]
    public void LabelsAndKeys()
    {
        Assert.Equal(10, C.Codecs.Length);
        Assert.Equal(["H.264 (h264_nvenc)", "HEVC (hevc_nvenc)"], C.Codecs.Take(2));      // the old labels stay valid
        Assert.Equal("hevc_nvenc", Text.CodecKey("HEVC (hevc_nvenc)"));
        Assert.Equal("h264_mf_hw", Text.CodecKey("H.264 (Windows, GPU)"));
        Assert.Equal("hevc_nvenc", Text.CodecKey("HEVC (something old)"));
        Assert.Equal("HEVC", Text.CodecShort("HEVC (hevc_amf)"));
    }

    [Fact]
    public void FallbackKeepsTheKindOfCodec()
    {
        Assert.Equal("HEVC (hevc_nvenc)", Encoders.Fallback("HEVC (hevc_nvenc)", ["hevc_nvenc", "h264_nvenc"]));
        Assert.Equal("HEVC (hevc_amf)", Encoders.Fallback("HEVC (hevc_nvenc)", ["h264_amf", "hevc_amf", "h264_mf"]));
        Assert.Equal("H.264 (Windows, GPU)", Encoders.Fallback("H.264 (h264_nvenc)", ["h264_mf_hw", "h264_mf", "hevc_mf_hw"]));
        Assert.Equal("H.264 (Windows, CPU)", Encoders.Fallback("HEVC (hevc_nvenc)", ["h264_mf"]));   // no HEVC at all here
        Assert.Equal("HEVC (hevc_nvenc)", Encoders.Fallback("HEVC (hevc_nvenc)", []));            // nothing known: unchanged
    }

    [Fact]
    public void ProbeEncodesAGreyPictureToNothing()
    {
        var cmd = Encoders.ProbeCommand("ffmpeg", "hevc_qsv");
        Assert.Equal("ffmpeg", cmd[0]);
        Assert.Contains("hevc_qsv", cmd);
        Assert.Equal(["-f", "null", "-"], cmd.TakeLast(3));
    }

    // ------------------------------------------------------------------ equalizer
    [Fact]
    public void NeutralEqualizerHasNoFilter()
    {
        Assert.Null(Equalizer.Filter([0, 0, 0, 0, 0], 0));
        var s = AppState.FromSettings(null);
        s.EqOn = true;
        Assert.Null(s.EqFilter());
        Assert.Null(s.ToExportSettings(false, out _)!.AudioAf);
    }

    [Fact]
    public void EqualizerFilterStrings()
    {
        Assert.Equal("equalizer=f=60:t=o:w=1.5:g=6,equalizer=f=4000:t=o:w=1.5:g=-3,alimiter=limit=0.97:level=0",
                     Equalizer.Filter([6, 0, 0, -3, 0], 0));
        Assert.Equal("volume=-6dB", Equalizer.Filter([0, 0, 0, 0, 0], -6));                 // only cuts: no limiter
        Assert.Equal("equalizer=f=1000:t=o:w=1.5:g=-12,volume=4dB,alimiter=limit=0.97:level=0",
                     Equalizer.Filter([0, 0, -50, 0, 0], 4));                                // clamped band
        Assert.Equal("volume=20dB,alimiter=limit=0.97:level=0", Equalizer.Filter([], 99));
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("+3 dB", 3)]
    [InlineData("-6dB", -6)]
    [InlineData(" 2,5 db ", 3)]
    [InlineData("", 0)]
    public void TypedDb(string text, int want) => Assert.Equal(want, Equalizer.ParseDb(text));

    [Fact]
    public void DbIsShownWithSign() =>
        Assert.Equal(("+3 dB", "-6 dB", "0 dB"), (Equalizer.FormatDb(3), Equalizer.FormatDb(-6), Equalizer.FormatDb(0)));

    [Fact]
    public void EqualizerOnEveryExportTrackAndInThePreview()
    {
        var clip = new ExportClip { Path = "a.mp4", Start = 0, End = 10, W = 1920, H = 1080, Fps = 60, Audio = true, NAudio = 2 };
        var fx = EffectSettings.Default with { AudioAf = "volume=3dB" };
        var g = GraphBuilder.Build([clip], 1280, 720, null, null, 0, 60, fx);
        Assert.Equal(["a0_0eq", "a0_1eq"], g.AudioLabels!);
        Assert.Contains("[a0_0]volume=3dB[a0_0eq]", g.Parts);
        Assert.Contains("[a0_1]volume=3dB[a0_1eq]", g.Parts);
        var plain = GraphBuilder.Build([clip], 1280, 720, null, null, 0, 60, EffectSettings.Default);
        Assert.Equal(["a0_0", "a0_1"], plain.AudioLabels!);                                // no equalizer: unchanged

        var simple = PreviewCommands.PreviewAudio("a.mp4", 0, 5, null, true, 1, null, fx);
        Assert.Equal("volume=3dB", simple[simple.IndexOf("-af") + 1]);
        var mixed = PreviewCommands.PreviewAudio("a.mp4", 0, 5, null, true, 2, null, fx);
        Assert.Equal("[aeq]", mixed[mixed.IndexOf("-map") + 1]);
        Assert.Contains("[aout]volume=3dB[aeq]", mixed[mixed.IndexOf("-filter_complex") + 1]);
    }

    [Fact]
    public void EqualizerSettingsAndProjects()
    {
        var s = AppState.FromSettings(null);
        s.EqBands[1] = 4; s.EqVolume = -3;
        Assert.Null(s.EqFilter());                                                            // plugin off
        s.EqOn = true;
        Assert.Equal(s.EqFilter(), s.ToExportSettings(false, out _)!.AudioAf);
        var d = new JsonObject();
        s.WriteTo(d);
        var back = AppState.FromSettings(d);
        Assert.Equal([0, 4, 0, 0, 0], back.EqBands);
        Assert.Equal(-3, back.EqVolume);
        var bad = AppState.FromSettings(new JsonObject { ["eq_bands"] = new JsonArray(99, "x", -99), ["eq_vol"] = 50 });
        Assert.Equal([12, 0, -12, 0, 0], bad.EqBands);
        Assert.Equal(20, bad.EqVolume);

        var fresh = AppState.FromSettings(null);
        var st = (JsonObject)ProjectSave.Data([Clip()], 0, fresh, null)["settings"]!;
        Assert.False(st.ContainsKey("eq_bands"));                                            // unchanged: the file is as before
        st = (JsonObject)ProjectSave.Data([Clip()], 0, s, null)["settings"]!;
        Assert.Equal(-3, (int)st["eq_vol"]!);
        var other = AppState.FromSettings(null);
        other.EqVolume = 9;
        other.ApplyProject(new JsonObject());
        Assert.Equal(0, other.EqVolume);                                                     // a project without them: neutral
    }

    [Fact]
    public void DenoiseLeftOutWithoutTheFilter()
    {
        var s = AppState.FromSettings(null);
        s.PictureOn = true;
        s.PicDenoise = 50;
        Assert.Contains("nlmeans", s.PictureFilter());
        s.DenoiseOk = false;
        Assert.Null(s.PictureFilter());
        s.PicSat = 20;
        Assert.DoesNotContain("nlmeans", s.PictureFilter());
    }

    [Fact]
    public void EqualizerIsABuiltinPluginOnByDefault()
    {
        Assert.Contains(PluginIds.Equalizer, PluginIds.Builtin);
        Assert.True(PluginIds.DefaultOn(PluginIds.Equalizer));    // (2.6: every plugin on for new users)
        Assert.True(PluginIds.DefaultOn(PluginIds.TimelineEffects));
    }
}
