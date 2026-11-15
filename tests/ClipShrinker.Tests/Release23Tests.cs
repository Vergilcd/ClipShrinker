using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>2.3: "Keep original file name".</summary>
public class Release23Tests
{
    static readonly DateTime When = new(2026, 11, 15, 9, 5, 0);

    [Fact]
    public void TickedKeepsTheClipName()
    {
        var s = AppState.FromSettings(null);
        Assert.True(s.KeepName);                                    // the default: as before
        s.Prefix = "{date}";
        s.Suffix = "{codec}";
        Assert.Equal("2026-11-15_a_HEVC", s.OutStem("a", new NameContext(When)));
    }

    [Fact]
    public void UntickedUsesOnlyTheLabels()
    {
        var s = AppState.FromSettings(null);
        s.KeepName = false;
        s.Prefix = "{date}";
        s.Suffix = "{codec}";
        Assert.Equal("2026-11-15_HEVC", s.OutStem("a", new NameContext(When)));
        s.Suffix = "";
        Assert.Equal("2026-11-15", s.OutStem("a", new NameContext(When)));
        s.Prefix = "";
        s.Suffix = "clip";
        Assert.Equal("clip", s.OutStem("a", new NameContext(When)));
    }

    [Fact]
    public void BothEmptyAlwaysKeepsTheName()
    {
        var s = AppState.FromSettings(null);
        s.KeepName = false;
        Assert.False(s.HasNameText);
        Assert.True(s.KeepsName);                                   // the tick is shown ticked and greyed out
        Assert.Equal("a", s.OutStem("a"));
        s.Prefix = "  ";                                            // only spaces count as empty
        Assert.Equal("a", s.OutStem("a"));
        s.Prefix = "x";
        Assert.False(s.KeepsName);                                  // the saved choice comes back
    }

    [Fact]
    public void SeparateFilesWithTheSameLabelsGetNumbered()
    {
        var s = AppState.FromSettings(null);
        s.KeepName = false;
        s.Suffix = "{codec}";
        var taken = new HashSet<string>();
        string p1 = ExportPlanner.AutoPath(s, @"C:\out", "a", taken, _ => false);
        taken.Add(p1.ToLowerInvariant());
        string p2 = ExportPlanner.AutoPath(s, @"C:\out", "b", taken, _ => false);
        Assert.Equal([@"C:\out\HEVC.mp4", @"C:\out\HEVC (2).mp4"], [p1, p2]);
    }

    [Fact]
    public void TheChoiceIsSaved()
    {
        var s = AppState.FromSettings(new JsonObject { ["keep_name"] = false });
        Assert.False(s.KeepName);
        var d = new JsonObject();
        s.WriteTo(d);
        Assert.False((bool)d["keep_name"]!);
        Assert.True(AppState.FromSettings(new JsonObject()).KeepName);
    }
}
