using System.Text.Json;
using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Py;

namespace ClipShrinker.Tests;

/// <summary>projects.json "project_data": what Save project writes, against the 1.0.8 App.project_data.</summary>
public class ProjectSaveTests
{
    static readonly JsonElement P = Fixtures.Load("projects.json");

    /// <summary>JSON equality with numbers compared by value (Python writes 5.0, .NET writes 5).</summary>
    static string? Diff(JsonNode? a, JsonNode? b, string path = "$")
    {
        if (a is null || b is null) return a is null && b is null ? null : $"{path}: {a?.ToJsonString() ?? "null"} vs {b?.ToJsonString() ?? "null"}";
        if (a is JsonObject oa && b is JsonObject ob)
        {
            var keys = oa.Select(p => p.Key).ToList();
            if (!keys.SequenceEqual(ob.Select(p => p.Key))) return $"{path}: keys {string.Join(",", keys)} vs {string.Join(",", ob.Select(p => p.Key))}";
            foreach (var k in keys) if (Diff(oa[k], ob[k], $"{path}.{k}") is { } d) return d;
            return null;
        }
        if (a is JsonArray aa && b is JsonArray ab)
        {
            if (aa.Count != ab.Count) return $"{path}: {aa.Count} vs {ab.Count} items";
            for (int i = 0; i < aa.Count; i++) if (Diff(aa[i], ab[i], $"{path}[{i}]") is { } d) return d;
            return null;
        }
        var va = (JsonValue)a;
        var vb = (JsonValue)b;
        if (va.GetValueKind() == JsonValueKind.Number && vb.GetValueKind() == JsonValueKind.Number)
            return double.Parse(va.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) ==
                   double.Parse(vb.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)
                ? null : $"{path}: {a.ToJsonString()} vs {b.ToJsonString()}";
        return a.ToJsonString() == b.ToJsonString() ? null : $"{path}: {a.ToJsonString()} vs {b.ToJsonString()}";
    }

    [Fact]
    public void SameJsonAsPython()
    {
        var m = new Mismatches("project_data");
        foreach (var c in P.GetProperty("project_data").EnumerateArray())
        {
            var rows = c.GetProperty("rows").EnumerateArray().Select(r =>
            {
                var t = new TimelineClip(r.GetProperty("path").GetString()!, new MediaInfo(1920, 1080, 60, 30, 1, 0, 0, null, null, null))
                {
                    Start = r.GetProperty("start").Num(), End = r.GetProperty("end").Num(), Preset = r.GetProperty("preset").Str(),
                };
                foreach (var s in r.GetProperty("sections").EnumerateArray())
                    t.Sections.Add(new SpeedSection(s.GetProperty("a").Num(), s.GetProperty("b").Num(), s.GetProperty("speed").Num()));
                foreach (var z in r.GetProperty("zooms").EnumerateArray())
                    t.Zooms.Add(new ZoomSection(z.GetProperty("a").Num(), z.GetProperty("b").Num(), z.GetProperty("x").Num(), z.GetProperty("y").Num(), z.GetProperty("w").Num()));
                return t;
            }).ToList();
            var st = c.GetProperty("state");
            string S(string k) => st.GetProperty(k).ValueKind == JsonValueKind.String ? st.GetProperty(k).GetString()! : st.GetProperty(k).GetRawText();
            var s0 = new AppState
            {
                Size = S("size"), Bitrate = S("bitrate"), Res = S("res"), Codec = S("codec"), Crop = S("crop"), Audio = S("audio"),
                Atrk = S("atrk"), MuteMic = st.GetProperty("mutemic").GetBoolean(), FastAudio = S("fast_aud"), SlowAudio = S("slow_aud"),
                SlowPitch = S("pitch"), FastPitch = S("fast_pitch"), Vhs = st.GetProperty("vhs").GetBoolean(),
                FastVol = st.GetProperty("fast_vol").Int(), SlowVol = st.GetProperty("slow_vol").Int(), ZoomFx = S("zoom_fx"),
                ZoomSnd = S("zoom_snd"), ZoomLen = S("zoom_len"), ZoomVol = st.GetProperty("zoom_vol").Int(), SlowInterp = S("interp"),
                Hdr = st.GetProperty("hdr").GetBoolean(), Mode = S("mode"), Trans = S("trans"), Tlen = S("tlen"),
            };
            var got = ProjectSave.Data(rows, c.GetProperty("active").Int(), s0, c.GetProperty("project_path").Str());
            var want = JsonNode.Parse(c.GetProperty("result").GetRawText());
            var d = Diff(want, got);
            m.Check(d is null, $"project {c.GetProperty("project_path").Str() ?? "(none)"}, active {c.GetProperty("active").Int()}: {d}");
        }
        m.AssertNone();
    }

    [Theory]
    [InlineData(1.0625, 3, 1.062)]       // an exact tie: to even
    [InlineData(1.0635, 3, 1.063)]       // stored as 1.06349999...: below the tie
    [InlineData(11.0005, 3, 11.001)]     // stored as 11.000500000000000966...: above the tie
    [InlineData(2.675, 2, 2.67)]         // binary value is below the tie
    [InlineData(-0.0625, 3, -0.062)]
    [InlineData(18.2499, 3, 18.25)]
    public void RoundLikePython(double x, int n, double want) => Assert.Equal(want, Py.RoundTo(x, n));

    [Fact]
    public void SavedProjectLoadsBack()
    {
        string dir = Directory.CreateTempSubdirectory("cs_proj_").FullName;
        try
        {
            string video = Path.Combine(dir, "v.mp4");
            File.WriteAllText(video, "");
            var info = new MediaInfo(1920, 1080, 60, 30, 1, 0, 0, null, null, null);
            var row = new TimelineClip(video, info) { Start = 1.5, End = 20.25 };
            row.Sections.Add(new SpeedSection(3, 6, 2.5));
            row.Zooms.Add(new ZoomSection(8, 10, 0.1, 0.2, 0.5));
            var s = AppState.FromSettings(null);
            s.Size = "33";
            string proj = Path.Combine(dir, "p.clipproj");
            ProjectSave.Save(proj, ProjectSave.Data([row], 0, s, proj));
            var load = ProjectFile.Load(proj, _ => info, _ => null);
            var back = load.Clips.Single();
            Assert.Equal((1.5, 20.25), (back.Start, back.End));
            Assert.Equal(row.Sections.Single().ToString(), back.Sections.Single().ToString());
            Assert.Equal(row.Zooms.Single().ToString(), back.Zooms.Single().ToString());
            var s2 = AppState.FromSettings(null);
            s2.ApplyProject(load.Settings);
            Assert.Equal("33", s2.Size);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class UndoTests
{
    sealed class Row(string name) { public override string ToString() => name; }

    static Snapshot<Row> Snap(Row? active, params (Row R, double Start)[] rows) =>
        new(rows.Select(r => r.R).ToList(), active,
            rows.ToDictionary(r => r.R, r => new ClipState(r.Start, 10, [], null, [])));

    [Fact]
    public void UndoRedoAndSkippingNoChange()
    {
        var a = new Row("a");
        var b = new Row("b");
        var h = new UndoHistory<Row>();
        var s0 = Snap(a, (a, 0));
        h.Push("add clips", s0);                     // before: [a]
        var s1 = Snap(a, (a, 0), (b, 0));
        h.Push("trim", s1);                          // a click on a handle without moving: same as now
        var now = Snap(a, (a, 0), (b, 0));
        var u = h.Step(true, now)!.Value;            // the "trim" step changes nothing and is skipped
        Assert.Equal("add clips", u.Label);
        Assert.Same(s0, u.Snap);
        Assert.True(h.CanRedo);
        var r = h.Step(false, s0)!.Value;
        Assert.Equal("add clips", r.Label);
        Assert.Equal(2, r.Snap.Rows.Count);
        Assert.Null(h.Step(false, r.Snap));
        h.Push("x", now);
        Assert.False(h.CanRedo);                     // a new change clears redo
    }

    [Fact]
    public void KeepsAtMost60Steps()
    {
        var a = new Row("a");
        var h = new UndoHistory<Row>();
        for (int i = 0; i < 100; i++) h.Push($"s{i}", Snap(a, (a, i)));
        int n = 0;
        var cur = Snap(a, (a, 1000));
        while (h.Step(true, cur) is { } e) { cur = e.Snap; n++; }
        Assert.Equal(UndoHistory<Row>.Max, n);
        Assert.Equal(40, cur.State[a].Start);         // the oldest 40 were dropped
    }

    [Fact]
    public void ClipStateRoundTrip()
    {
        var c = new TimelineClip("x", new MediaInfo(1, 1, 1, 20, 0, 0, 0, null, null, null)) { Start = 1, End = 9, Preset = "p" };
        c.Sections.Add(new SpeedSection(5, 6, 2));
        c.Sections.Add(new SpeedSection(2, 3, 0.5));
        var st = ClipState.Capture(c);
        c.Sections.Clear();
        c.Start = 4;
        st.Apply(c);
        Assert.Equal((1.0, 9.0, "p"), (c.Start, c.End, c.Preset));
        Assert.Equal([2.0, 5.0], c.Sections.Select(s => s.A));          // sorted, new objects
    }
}

public class GrabbableTests
{
    [Fact]
    public void HandOnlyOverWhatCanBeDragged()
    {
        var clip = new TimelineClip("x", new MediaInfo(1920, 1080, 60, 20, 1, 0, 0, null, null, null)) { Start = 2, End = 18 };
        clip.Sections.Add(new SpeedSection(6, 10, 2));
        var ed = TimelineEditor.EdgeToEdge(clip);
        ed.Active = true;
        double W = 1000, strip = 90, lane = 15;
        Assert.True(ed.Grabbable(ed.X(2, W) + 3, strip, W));        // trim start handle
        Assert.True(ed.Grabbable(ed.X(18, W) - 3, strip, W));       // trim end handle
        Assert.True(ed.Grabbable(ed.X(6, W), strip, W));            // section edge
        Assert.True(ed.Grabbable(ed.X(8, W), lane, W));             // section bar in the lane (moves it)
        Assert.False(ed.Grabbable(ed.X(8, W), strip, W));           // inside the section on the strip: seeks
        Assert.False(ed.Grabbable(ed.X(13, W), strip, W));          // plain strip: seeks
        ed.Active = false;
        Assert.False(ed.Grabbable(ed.X(2, W), 20, W));              // a thin strip only selects the clip
    }

    [Fact]
    public void OverlayHeadersTopAndBottom()
    {
        var clip = new TimelineClip("x", new MediaInfo(1920, 1080, 60, 20, 1, 0, 0, null, null, null));
        var ed = TimelineEditor.EdgeToEdge(clip);
        ed.Active = true;
        double W = 1000;
        Assert.Equal((124.0, 124.0, 0.0), (ed.Height, ed.ThumbHeight, ed.StripTop));   // thumbnails fill the timeline
        ed.Playhead = 4;
        var z = ed.AddZoom()!;
        var (zt, zb) = ed.ZoomLane;
        Assert.Equal((110.0, 124.0), (zt, zb));                       // the zoom header at the bottom, flush with the edge
        ed.Down(ed.X(z.A + 0.5, W), zt + 5, W);                        // press on it: moves the zoom section
        Assert.Equal(DragKind.ZoomMove, ed.Drag);
        ed.Up();
        ed.ZSel = null;
        ed.Down(ed.X(z.A + 0.5, W), 60, W);                            // a click inside the zoom area selects it
        Assert.Same(z, ed.ZSel);
        Assert.Equal(DragKind.Play, ed.Drag);
        ed.Up();
        ed.Down(ed.X(z.B + 2, W), 60, W);                              // and outside it deselects
        Assert.Null(ed.ZSel);
        ed.Up();
        ed.Playhead = 12;
        var s = ed.AddSection(2.0)!;
        ed.Down(ed.X(s.A + 0.4, W), 8, W);                             // the speed header on top: moves the section
        Assert.Equal(DragKind.SecMove, ed.Drag);
        ed.Up();
        ed.Down(ed.X(s.A + 0.4, W), 60, W);                            // the picture in the middle: seeks
        Assert.Equal(DragKind.Play, ed.Drag);
        ed.Up();
        Assert.True(ed.RightClick(ed.X(z.A + 0.5, W), zt + 5, W));     // right-click on the zoom header removes it
        Assert.Empty(clip.Zooms);
        ed.Wheel(500, 3, true, W);                                     // zoomed in: the overview bar takes the bottom
        Assert.Equal(ed.OverviewTop - 1 - TimelineEditor.OvZoomH, ed.ZoomLane.Top);
        ed.Down(300, ed.Height - 3, W);
        Assert.Equal(DragKind.Overview, ed.Drag);
    }
}

public class PresetRuleTests
{
    static readonly List<ClipShrinker.Core.Settings.Preset> List = [new("Discord", 4, "q"), new("High", 25, "")];

    [Theory]
    [InlineData(null, "New", "12,5", "w", null, null)]
    [InlineData(null, "New", "abc", "", "Bitrate", "Enter the bitrate in Mbps, e.g. 20.")]
    [InlineData(null, "New", "0.05", "", "Bitrate", "Enter the bitrate in Mbps, e.g. 20.")]
    [InlineData(null, "  ", "5", "", "Name", "Give the preset a name.")]
    [InlineData(null, "discord", "5", "", "Name", "There is already a preset with this name.")]
    [InlineData(0, "discord", "5", "", null, null)]                       // renaming itself is fine
    [InlineData(null, "New", "5", "S", "Hotkey", "This key already does something else - pick another one.")]
    [InlineData(null, "New", "5", "Q", "Hotkey", "The key is already used by \"Discord\".")]
    [InlineData(0, "Discord", "5", "q", null, null)]
    public void Validate(int? index, string name, string mbps, string key, string? title, string? error)
    {
        var (p, t, e) = ClipShrinker.Core.Settings.PresetStore.Validate(List, index, name, mbps, key);
        Assert.Equal((title, error), (t, e));
        if (title is null) Assert.Equal(key.ToLowerInvariant(), p!.Key);
    }
}

public class KeyTests
{
    [Theory]
    [InlineData('s', "s")]
    [InlineData('S', "s")]
    [InlineData('1', "1")]
    [InlineData('!', "exclam")]
    [InlineData('\'', "apostrophe")]
    [InlineData('ö', "odiaeresis")]
    [InlineData('Ö', "odiaeresis")]
    [InlineData('ő', "odoubleacute")]
    [InlineData('[', "bracketleft")]
    public void TkKeyNames(char c, string want) => Assert.Equal(want, Text.KeysymForChar(c));

    [Fact]
    public void KeyConflicts()
    {
        var keys = new Dictionary<string, string>(C.KeyDefaults);
        // 2.0: every shortcut can be rebound; only Esc / Enter / Tab / Alt+F4 stay fixed
        Assert.Contains("used by the program", Text.KeyConflict("escape", "speed", keys, []));
        Assert.Contains("used by the program", Text.KeyConflict("return", "save", keys, []));
        Assert.Contains("used by the program", Text.KeyConflict("alt+f4", "save", keys, []));
        Assert.Equal("Ctrl + S is already used: save the project.", Text.KeyConflict("ctrl+s", "speed", keys, []));
        Assert.Equal("Space is already used: play / pause (loops the trimmed part).", Text.KeyConflict("space", "speed", keys, []));
        Assert.Contains("hotkey of a bitrate preset", Text.KeyConflict("q", "speed", keys, ["q"]));
        Assert.Equal("D is already used: add a slow-down section (pink) at the playhead.", Text.KeyConflict("d", "speed", keys, []));
        Assert.Null(Text.KeyConflict("s", "speed", keys, []));
        Assert.Null(Text.KeyConflict("shift+m", "speed", keys, []));
        Assert.Null(Text.KeyConflict("ctrl+s", "save", keys, []));
        Assert.Null(Text.KeyConflict("o", "play", keys, []));
        Assert.Equal("P is already used: add a pause (orange) at the playhead.", Text.KeyConflict("p", "speed", keys, []));   // (2.9)
        Assert.Null(Text.KeyConflict("shift+exclam", "speed", keys, []));
    }

    [Fact]
    public void EveryShortcutHasTextAndUniqueDefault()
    {
        Assert.Equal(C.KeyDefaults.Count, C.KeyDefaults.Values.Distinct().Count());
        Assert.All(C.KeyDefaults.Keys, a => Assert.True(Text.KeyText.ContainsKey(a), a));
        Assert.DoesNotContain(C.KeyDefaults.Values, C.FixedKeys.Contains);
    }

    [Fact]
    public void PresetKeysFollowTheCurrentShortcuts()
    {
        // speed moved from S to X: a preset may now take S but not X
        var reserved = new HashSet<string>(C.FixedKeys.Concat(C.KeyDefaults.Values.Where(v => v != "s").Append("x")));
        Assert.NotNull(ClipShrinker.Core.Settings.PresetStore.Validate([], null, "Fast", "8", "s", reserved).Preset);
        Assert.Equal("Hotkey", ClipShrinker.Core.Settings.PresetStore.Validate([], null, "Fast", "8", "x", reserved).Title);
    }
}
