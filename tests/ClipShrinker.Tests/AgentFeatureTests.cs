using System.Text.Json.Nodes;
using ClipShrinker.Core.Agent;
using ClipShrinker.Core.Analysis;
using ClipShrinker.Core.Modules;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>2.8: white balance, speech to text parsing, downloads, the AI editing module's default.</summary>
public class AgentFeatureTests
{
    [Fact]
    public void WhiteBalanceIsNeutralAtZeroAndKeepsOldFilters()
    {
        Assert.Null(PictureAdjust.Filter(0, 0, 0, 0, 0));
        Assert.Equal(PictureAdjust.Filter(50, 20, -30), PictureAdjust.Filter(50, 20, -30, 0, 0));   // exports from 2.7 stay the same
        Assert.Equal(PictureAdjust.Filter(10, 0, 0, 50, gpu: false), PictureAdjust.Filter(10, 0, 0, 0, 0, 50, gpu: false));

        string warm = PictureAdjust.Filter(0, 0, 0, 50, 0)!;                    // warmer: less blue (U down), more red (V up)
        Assert.Equal("lutyuv=y='val':u='clip(val+-0.0300*(maxval-minval),minval,maxval)':v='clip(val+0.0300*(maxval-minval),minval,maxval)'", warm);
        string magenta = PictureAdjust.Filter(0, 0, 0, 0, 100)!;                 // tint: both planes the same way
        Assert.Equal(2, magenta.Split("+0.0500*").Length - 1);
        Assert.Equal(PictureAdjust.Filter(0, 0, 0, 100, 0), PictureAdjust.Filter(0, 0, 0, 400, 0));   // clamped
    }

    [Fact]
    public void WhiteBalanceIsSavedInProjectsAndSettings()
    {
        var s = AppState.FromSettings(null);
        Assert.False(s.PictureChanged);
        s.PicTemp = -25;
        s.PicTint = 10;
        Assert.True(s.PictureChanged);
        var d = new JsonObject();
        s.WriteTo(d);
        var back = AppState.FromSettings(d);
        Assert.Equal((-25, 10), (back.PicTemp, back.PicTint));
        var proj = AppState.FromSettings(null);
        proj.ApplyProject(new JsonObject { ["pic_temp"] = 40 });
        Assert.Equal((40, 0), (proj.PicTemp, proj.PicTint));
        proj.ApplyProject(new JsonObject());                                     // a project without them: neutral
        Assert.Equal(0, proj.PicTemp);
    }

    [Fact]
    public void SrtLinesGetSourceTimes()
    {
        const string srt = "1\n00:00:01,500 --> 00:00:03,000\n Behind you!\n\n2\n00:00:04,000 --> 00:00:05,250\n[BLANK_AUDIO]\n\n" +
                           "3\n00:01:02,000 --> 00:01:04,000\nI got him,\nnice one\n\n4\n00:01:05,000 --> 00:01:06,000\n...\n";
        var lines = Speech.ParseSrt(srt, 100);
        Assert.Equal(2, lines.Count);
        Assert.Equal(new Speech.Line(101.5, 103, "Behind you!"), lines[0]);
        Assert.Equal(new Speech.Line(162, 164, "I got him, nice one"), lines[1]);
        Assert.Empty(Speech.ParseSrt("", 0));
    }

    [Fact]
    public void DownloadedSoundNamesAreSafe()
    {
        Assert.Equal("air horn", Downloads.CleanName("air%20horn"));
        Assert.Equal("a b", Downloads.CleanName("a<>:\"/\\|?*b"));
        Assert.Equal("sound", Downloads.CleanName("..."));
        Assert.Equal(60, Downloads.CleanName(new string('x', 100)).Length);
    }

    [Fact]
    public void AiEditingStartsOff()
    {
        Assert.False(ModuleIds.DefaultOn(ModuleIds.AiAgent));
        Assert.True(ModuleIds.DefaultOn(ModuleIds.SoundTrack));
        Assert.Contains("Anthropic", ModuleIds.AiAgentNotice);
    }
}
