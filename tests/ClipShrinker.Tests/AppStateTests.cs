using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Settings;

namespace ClipShrinker.Tests;

/// <summary>Summary lines, settings round trip and export planning (the Phase 3 pieces that moved into Core).</summary>
public class AppStateTests
{
    static TimelineClip Row(string path, double dur, int w = 1920, int h = 1080, int naudio = 1) =>
        new(path, new MediaInfo(w, h, 60, dur, naudio, 8e6, 0, null, null, null));

    [Fact]
    public void SummaryLines()
    {
        var s = AppState.FromSettings(null);
        Assert.Equal(["No clips yet", "HEVC  ·  Auto resolution  ·  crop 16:9", "Max 50 MB per file", "clip.mp4"], s.SummaryLines([]));
        var a = Row(@"C:\v\a.mp4", 20);
        var b = Row(@"C:\v\b.mp4", 10);
        b.Sections.Add(new SpeedSection(2, 6, 2.0));      // 10 s -> 8 s
        s.Trans = "Crossfade (gradient)";
        s.Tlen = "0,5";
        var lines = s.SummaryLines([a.ToExport(), b.ToExport()]);
        Assert.Equal("2 clips joined  ·  0:27.50", lines[0]);  // 20 + 8 - 0.5
        s.Bitrate = "8";
        s.BitrateChanged();
        lines = s.SummaryLines([a.ToExport()]);
        Assert.Equal("HEVC  ·  Original resolution  ·  crop 16:9", lines[1]);
        Assert.Equal("Fixed 8 Mbps video  ·  about 19 MB in total", lines[2]);   // 20 s * (8 Mbps + 128 kbps) / 8
        s.Prefix = "my:clip"; s.Suffix = "final"; s.Crop = "Original (no crop)";
        lines = s.SummaryLines([a.ToExport()]);
        Assert.Equal("myclip_a_final.mp4", lines[3]);
        Assert.EndsWith("no crop", lines[1]);
    }

    [Fact]
    public void SettingsRoundTripKeepsUnknownKeysAndWritesPlainText()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cs_settings_{Guid.NewGuid():N}.json");
        try
        {
            var d = new JsonObject { ["geometry"] = "1400x900+10+20", ["something_else"] = 5 };
            var s = AppState.FromSettings(d);
            s.Size = "75"; s.Prefix = "été";
            s.WriteTo(d);
            SettingsStore.Save(path, d);
            string text = File.ReadAllText(path);
            Assert.Contains("\"1400x900+10+20\"", text);
            Assert.Contains("été", text);
            var back = AppState.FromSettings(SettingsStore.Load(path));
            Assert.Equal(("75", "été"), (back.Size, back.Prefix));
            Assert.Equal(5, (int)SettingsStore.Load(path)["something_else"]!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SeparateJobsGetUniqueNamesAndPresets()
    {
        var s = AppState.FromSettings(null);
        var rows = new List<TimelineClip> { Row(@"C:\v\a.mp4", 5), Row(@"D:\w\a.mp4", 5), Row(@"C:\v\b.mov", 5) };
        rows[2].Preset = "Low";
        s.Suffix = "{codec}";                               // 2.2: no codec name unless asked for
        var clips = ExportPlanner.PrepareClips(s, rows);
        var st = s.ToExportSettings(false, out _)!;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\out\a_HEVC.mp4" };
        var jobs = ExportPlanner.Separate(s, st, rows, clips, @"C:\out", n => n == "Low" ? new Preset("Low", 3, "") : null, existing.Contains);
        Assert.Equal([@"C:\out\a_HEVC (2).mp4", @"C:\out\a_HEVC (3).mp4", @"C:\out\b_HEVC.mp4"], jobs.Select(j => j.Out));
        Assert.Equal((3.0, (double?)null, "Original"), (jobs[2].Settings.BitrateMbps!.Value, jobs[2].Settings.TargetMb, jobs[2].Settings.ResMode));
        var (_, line) = ExportPlanner.Report(jobs, st, [(jobs[0].Out, 2 * 1048576)], ["b_HEVC.mp4: boom"]);
        Assert.Equal("1 saved (per-clip bitrates): a_HEVC (2).mp4 (2.00 MB)  |  1 failed: b_HEVC.mp4: boom", line);
    }

    [Fact]
    public void MuteMicKeepsOneTrack()
    {
        var s = AppState.FromSettings(new JsonObject { ["mute_mic"] = true });
        var clips = ExportPlanner.PrepareClips(s, [Row(@"C:\v\a.mp4", 5, naudio: 2), Row(@"C:\v\b.mp4", 5, naudio: 0)]);
        Assert.Equal([1, 0], clips.Select(c => c.AudioTracks));
    }
}
