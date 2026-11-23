using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>2.9.1: the Export panel's "Size limit | Fixed bitrate" switch and the result line of the SUMMARY.</summary>
public class SizeSwitchTests
{
    static TimelineClip Row(string path, double dur) =>
        new(path, new MediaInfo(1920, 1080, 60, dur, 1, 8e6, 0, null, null, null));

    [Fact]
    public void SwitchKeepsBothNumbersAndTheExportFollowsIt()
    {
        var s = AppState.FromSettings(null);
        Assert.False(s.FixedRate);

        s.SetFixedRate(true);                               // nothing typed yet: the export asks for a bitrate
        Assert.Null(s.ToExportSettings(false, out var err));
        Assert.Contains("pick Size limit", err);

        s.Bitrate = "8"; s.BitrateChanged();
        Assert.Equal((8.0, (double?)null), (s.ToExportSettings(false, out _)!.BitrateMbps!.Value, s.ToExportSettings(false, out _)!.TargetMb));

        s.SetFixedRate(false);                              // back to the size limit: the bitrate is kept aside
        Assert.Equal(("", "8"), (s.Bitrate, s.KeptBitrate));
        var st = s.ToExportSettings(false, out _)!;
        Assert.Equal(((double?)null, (double?)50), (st.BitrateMbps, st.TargetMb));

        s.SetFixedRate(true);                               // and comes back
        Assert.Equal("8", s.Bitrate);
    }

    [Fact]
    public void OldSettingsAndProjectsPickTheMatchingSide()
    {
        Assert.True(AppState.FromSettings(new JsonObject { ["bitrate"] = "40" }).FixedRate);
        Assert.False(AppState.FromSettings(new JsonObject { ["bitrate"] = "" }).FixedRate);

        var s = AppState.FromSettings(new JsonObject { ["bitrate"] = "40" });
        s.ApplyProject(new JsonObject { ["bitrate"] = "" });   // a project made with the size limit
        Assert.Equal((false, "", "40"), (s.FixedRate, s.Bitrate, s.KeptBitrate));
        s.ApplyProject(new JsonObject { ["bitrate"] = "12" });
        Assert.Equal((true, "12"), (s.FixedRate, s.Bitrate));
    }

    [Fact]
    public void SettingsSaveTheExportValueAndTheKeptOne()
    {
        var s = AppState.FromSettings(new JsonObject { ["bitrate"] = "40" });
        s.SetFixedRate(false);
        var d = new JsonObject();
        s.WriteTo(d);
        Assert.Equal(("", "40"), ((string)d["bitrate"]!, (string)d["bitrate_kept"]!));   // older versions read size mode
        var back = AppState.FromSettings(d);
        Assert.Equal((false, "40"), (back.FixedRate, back.KeptBitrate));
    }

    [Fact]
    public void SummaryResultLine()
    {
        var s = AppState.FromSettings(null);
        Assert.Equal(("Under 50 MB", "per file", false), s.SummaryResult([]));

        var a = Row(@"C:\v\a.mp4", 20);
        s.Size = "5";                                       // 5 MB / 20 s = 2.10 Mbps, minus 128k audio
        Assert.Equal(("Under 5 MB", "about 2.0 Mbps video for 0:20.00", false), s.SummaryResult([a.ToExport()]));

        s.Size = "0.1";
        Assert.Equal(("Under 0.1 MB", "too small for 0:20.00 of video", true), s.SummaryResult([a.ToExport()]));

        s.Size = "50"; s.Mode = "separate";                 // separate files: the longest clip decides
        var b = Row(@"C:\v\b.mp4", 10);
        Assert.Equal("per file  ·  about 21 Mbps video for the longest clip (0:20.00)", s.SummaryResult([a.ToExport(), b.ToExport()]).Sub);

        s.Mode = "join";                                    // one video: the joined length (30 s, no transition)
        s.Trans = "Hard cut";
        Assert.Equal("one video  ·  about 14 Mbps video for 0:30.00", s.SummaryResult([a.ToExport(), b.ToExport()]).Sub);

        s.Size = "";
        Assert.Equal(("Enter a max size", "in MB, e.g. 50", true), s.SummaryResult([a.ToExport()]));

        s.SetFixedRate(true);
        Assert.Equal(("Enter a bitrate", "in Mbps, e.g. 20", true), s.SummaryResult([a.ToExport()]));
        s.Bitrate = "8"; s.BitrateChanged();
        Assert.Equal(("About 19 MB", "in total  ·  no size limit (fixed 8 Mbps)", true), s.SummaryResult([a.ToExport()]));
        Assert.Equal(("Fixed 8 Mbps", "no size limit", true), s.SummaryResult([]));
    }

    [Fact]
    public void SummaryLinesFollowTheOutputSwitch()
    {
        var s = AppState.FromSettings(null);
        var a = Row(@"C:\v\a.mp4", 20);
        var b = Row(@"C:\v\b.mp4", 10);
        s.Trans = "Hard cut";
        Assert.Equal("2 clips joined  ·  0:30.00", s.SummaryLines([a.ToExport(), b.ToExport()])[0]);
        Assert.False(s.Separate(2));
        s.Mode = "separate";
        var lines = s.SummaryLines([a.ToExport(), b.ToExport()]);
        Assert.Equal(("2 clips  ·  2 files", "a.mp4 and 1 more"), (lines[0], lines[3]));
        Assert.True(s.Separate(2));
        Assert.False(s.Separate(1));                        // one clip is always one file
        Assert.Equal("1 clip  ·  0:20.00", s.SummaryLines([a.ToExport()])[0]);
    }

    [Fact]
    public void FixedBitrateWithOriginalAudioDoesNotThrow()
    {
        var s = AppState.FromSettings(new JsonObject { ["bitrate"] = "8", ["audio"] = "Original" });
        Assert.StartsWith("Fixed 8 Mbps video  ·  about", s.SummaryLines([Row(@"C:\v\a.mp4", 20).ToExport()])[2]);
    }
}
