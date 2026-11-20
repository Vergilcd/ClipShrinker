using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>2.9: pause sections (speed 0: the frame at A stays for B - A seconds, then the clip goes on from A) and
/// sound ends that follow the speed sections under them.</summary>
[Collection("ModuleFlags")]
public class PauseTests
{
    static TimelineClip Clip() => new("C:/v/a.mp4", new MediaInfo(1920, 1080, 60, 90, 1, 8e6, 0, null, null, null));

    [Fact]
    public void PauseIsAHoldPieceAndNothingIsCut()
    {
        var p = Timing.BuildPieces(68, 83, [new SpeedSection(76, 78, 0)]);
        Assert.Equal([new Piece(68, 76, 1), new Piece(76, 76, 1, 2), new Piece(76, 83, 1)], p);
        Assert.Equal(17, Timing.Duration(p), 6);                              // 15 s kept + 2 s pause
        Assert.False(Timing.IsPlain(p));
        Assert.Equal(8, Timing.SrcToOut(p, 76), 6);                           // a time at the pause = its start
        Assert.Equal(10.5, Timing.SrcToOut(p, 76.5), 6);
        Assert.Equal(76, Timing.OutToSrc(p, 9), 6);                           // during the pause: the one frame
        Assert.Equal(76.5, Timing.OutToSrc(p, 10.5), 6);
    }

    [Fact]
    public void PauseNextToSlowMotionAndAtTheTrimEnd()
    {
        var p = Timing.BuildPieces(68, 83, [new SpeedSection(74, 75.5, 0), new SpeedSection(75.5, 77.5, 0.5)]);
        Assert.Equal([new Piece(68, 74, 1), new Piece(74, 74, 1, 1.5), new Piece(74, 75.5, 1), new Piece(75.5, 77.5, 0.5),
                      new Piece(77.5, 83, 1)], p);
        Assert.Equal(15 + 1.5 + 2, Timing.Duration(p), 6);
        // a pause running past the trim end is cut there (the timeline shows it that way too)
        var q = Timing.BuildPieces(68, 83, [new SpeedSection(82, 85, 0)]);
        Assert.Equal(new Piece(82, 82, 1, 1), q[1]);
    }

    [Fact]
    public void FiltersHoldTheFrameAndSilenceTheSound()
    {
        var p = Timing.BuildPieces(0, 4, [new SpeedSection(1, 3, 0)]);
        string v = string.Join(";", Filters.SegVideo("b", p, 0, "c", "m", (1920, 1080), 60, null, EffectSettings.Default));
        string fr = ClipShrinker.Core.Py.PyFraction.Ratio(60);
        Assert.Contains($"[cs1]trim=start=1.0000:end=1.5000,setpts=PTS-STARTPTS,fps={fr},trim=end_frame=1,loop=loop=-1:size=1," +
                        $"setpts=N/({fr}*TB),trim=duration=2.0000[cp1]", v);
        Assert.Contains("concat=n=3:v=1:a=0[m]", v);
        string a = string.Join(";", Filters.SegAudio("ab", p, 0, "d", "o", true, EffectSettings.Default));
        Assert.Contains("[da1]atrim=start=1.0000:end=1.1000,asetpts=PTS-STARTPTS,volume=0,apad=whole_dur=2.0000,atrim=duration=2.0000[dq1]", a);
        Assert.Contains("concat=n=3:v=0:a=1[o]", a);
    }

    [Fact]
    public void SoundEndFollowsSlowMotion()
    {
        var c = Clip();
        (c.Start, c.End) = (68, 83);
        c.Sections.Add(new SpeedSection(75.5, 77.5, 0.5));
        var b = new SoundBlock { Path = "C:/s/alert.mp3", At = 75.8, Len = 2.4, FileDur = 2.4 };
        c.Sounds.Add(b);
        Assert.Equal(78.2, b.End, 6);                                         // the old, naive end
        Assert.Equal(77.0, c.SoundEnd(b), 6);                                 // 2.4 real s = 1.2 source s at half speed
        Assert.Equal(2.4, Sounds.LenTo(75.8, 77.0, c.TimePieces()), 6);
        // a pause under a sound: it ends during the pause, at the paused frame
        c.Sections.Clear();
        c.Sections.Add(new SpeedSection(76, 79, 0));
        Assert.Equal(76, c.SoundEnd(b), 6);
        // outside the trim the clip plays at 1x (as the export does)
        var early = new SoundBlock { Path = "C:/s/alert.mp3", At = 60, Len = 2, FileDur = 2.4 };
        Assert.Equal(62, c.SoundEnd(early), 6);
    }

    [Fact]
    public void PauseIsSavedAsSpeedZeroAndUndone()
    {
        var c = Clip();
        c.Sections.Add(new SpeedSection(76, 77.5, 0));
        var json = ProjectSave.Data([c], 0, AppState.FromSettings(null), null);
        var sec = json["clips"]![0]!["sections"]![0]!;
        Assert.Equal(0, sec["speed"]!.GetValue<double>());
        var st = ClipState.Capture(c);
        c.Sections.Clear();
        st.Apply(c);
        Assert.True(c.Sections[0].IsPause);
    }

    [Fact]
    public void EditorAddsPauseWithLengthBox()
    {
        var c = Clip();
        (c.Start, c.End) = (68, 83);
        var ed = new TimelineEditor(c) { Playhead = 76 };
        var s = ed.AddSection(0)!;
        Assert.True(s.IsPause);
        Assert.Equal((76.0, 77.0), (s.A, s.B));
        Assert.Equal("1s", TimelineEditor.SectionText(s));
        Assert.Equal("2.5s", ed.CommitSpeedText(s, "2,5 s"));
        Assert.Equal(78.5, s.B, 6);
        Assert.Equal("7s", ed.CommitSpeedText(s, "30"));                     // cut at the trim end (83 - 76)
        Assert.Equal(15 + 7, ed.EditedLen, 6);
    }
}
