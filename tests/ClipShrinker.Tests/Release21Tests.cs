using ClipShrinker.Core;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Settings;

namespace ClipShrinker.Tests;

/// <summary>2.1: file name fields, playing outside the trim, the taller thin strip.</summary>
public class Release21Tests
{
    static TimelineClip Row(string path, double dur, int w = 1920, int h = 1080) =>
        new(path, new MediaInfo(w, h, 60, dur, 1, 8e6, 0, null, null, null));

    static readonly DateTime When = new(2026, 11, 15, 9, 5, 0);

    [Fact]
    public void PlainPrefixAndSuffixAreUnchanged()
    {
        var s = AppState.FromSettings(null);
        Assert.Equal("a", s.OutStem("a"));                 // 2.2: an empty suffix adds nothing
        s.Prefix = "pre";
        s.Suffix = "post";
        Assert.Equal("pre_a_post", s.OutStem("a"));
        s.Suffix = "{unknown}";                         // an unknown field stays as typed
        Assert.Equal("pre_a_{unknown}", s.OutStem("a"));
    }

    [Fact]
    public void FieldsAreFilledIn()
    {
        var s = AppState.FromSettings(null);
        s.Prefix = "{date}";
        s.Suffix = "{TIME}_{preset}_{size}_{codec}";
        Assert.Equal("2026-11-15_a_09-05_Auto_50MB_HEVC", s.OutStem("a", new NameContext(When)));
        Assert.Equal("2026-11-15_a_09-05_Discord_50MB_HEVC", s.OutStem("a", new NameContext(When, "Discord")));
        s.Codec = C.Codecs[0];
        s.Prefix = "";
        s.Suffix = "{codec}";
        Assert.Equal("a_H264", s.OutStem("a", new NameContext(When)));
        s.Suffix = "{date}";
        s.Prefix = "  ";
        Assert.Equal("a_2026-11-15", s.OutStem("a", new NameContext(When)));
    }

    [Fact]
    public void ResolutionFollowsTheEncoderChoice()
    {
        var s = AppState.FromSettings(null);
        s.Suffix = "{res}";
        Assert.Equal("a_Auto", s.OutStem("a"));         // no clips: the setting
        var clip = Row(@"C:\v\a.mp4", 60).ToExport();
        var st = s.ToExportSettings(false, out _)!;
        // 50 MB for 60 s at 1080p60 is ~6.6 Mbps -> bpp 0.053 at 100 %: full height
        Assert.Equal("a_1080p", s.OutStem("a", new NameContext(When, null, [clip], st)));
        // 20 MB: ~2.6 Mbps -> 75 % is still too little, 50 %
        Assert.Equal("a_540p", s.OutStem("a", new NameContext(When, null, [clip], st with { TargetMb = 20 })));
        Assert.Equal("a_810p", s.OutStem("a", new NameContext(When, null, [clip], st with { BitrateMbps = 5, TargetMb = null, ResMode = "75%" })));
    }

    [Fact]
    public void SeparateFilesGetTheirPreset()
    {
        var s = AppState.FromSettings(null);
        s.Suffix = "{preset}";
        var rows = new List<TimelineClip> { Row(@"C:\v\a.mp4", 10), Row(@"C:\v\b.mp4", 10) };
        rows[1].Preset = "Small";
        var clips = ExportPlanner.PrepareClips(s, rows);
        var st = s.ToExportSettings(false, out _)!;
        var jobs = ExportPlanner.Separate(s, st, rows, clips, @"C:\out", n => n == "Small" ? new Preset("Small", 4, "") : null, _ => false);
        Assert.Equal([@"C:\out\a_Auto.mp4", @"C:\out\b_Small.mp4"], jobs.Select(j => j.Out));
    }

    [Fact]
    public void PlayOutsideTheTrimPlaysOnceToTheEnd()
    {
        var c = Row(@"C:\v\a.mp4", 30);
        c.Start = 5;
        c.End = 20;
        Assert.Equal((8.0, 20.0, false), PreviewPlan.PlayRange(c, 8));        // inside: loop the trim from the playhead
        Assert.Equal((5.0, 20.0, false), PreviewPlan.PlayRange(c, 19.9));     // right at the trim end: from the start
        Assert.Equal((2.0, 30.0, true), PreviewPlan.PlayRange(c, 2));         // before the trim: once, to the end
        Assert.Equal((25.0, 30.0, true), PreviewPlan.PlayRange(c, 25));       // after the trim
        Assert.Equal((5.0, 20.0, false), PreviewPlan.PlayRange(c, 29.9));     // at the very end: the trim again
    }

    [Fact]
    public void ThinStripIsTaller()
    {
        var ed = TimelineEditor.EdgeToEdge(Row(@"C:\v\a.mp4", 30));
        Assert.Equal(52, ed.Height);
        Assert.Equal(TimelineEditor.CompactTlH, new TimelineEditor(Row(@"C:\v\a.mp4", 30)).Height);   // 1.0.8 layout keeps 42
    }

    [Fact]
    public void FullScreenShortcutHasADefault()
    {
        Assert.Equal("f", C.KeyDefaults["fullscreen"]);
        Assert.True(Text.KeyText.ContainsKey("fullscreen"));
    }
}
