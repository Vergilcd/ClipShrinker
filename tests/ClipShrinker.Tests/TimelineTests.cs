using System.Text.Json;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>timeline.json (tools/dump_timeline_fixtures.py): the 1.0.8 ClipRow editing methods driven by scripted and
/// random mouse / key sequences; TimelineEditor replays every step and must reach the same state.</summary>
public class TimelineTests
{
    static readonly JsonElement T = Fixtures.Load("timeline.json");

    static string Fmt(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ReplaysEveryStep()
    {
        var m = new Mismatches("timeline");
        foreach (var c in T.GetProperty("cases").EnumerateArray())
        {
            string name = c.GetProperty("name").GetString()!;
            double W = c.GetProperty("W").Num(), dur = c.GetProperty("dur").Num();
            var clip = new TimelineClip("C:\\clips\\x.mp4", new MediaInfo(c.GetProperty("w").Int(), c.GetProperty("h").Int(), 60, dur, 1, 8e6, 0, null, null, null));
            var ed = new TimelineEditor(clip) { Active = true };
            var status = new List<string>();
            var undo = new List<string>();
            int previews = 0;
            ed.Status = status.Add;
            ed.PushUndo = undo.Add;
            ed.PreviewRequested = _ => previews++;
            int n = 0;
            foreach (var step in c.GetProperty("steps").EnumerateArray())
            {
                var op = step.GetProperty("op");
                string where = $"{name}, step {n++} {op.GetRawText()}";
                status.Clear();
                undo.Clear();
                previews = 0;
                bool failed = false;
                m.Run(where, () =>
                {
                    switch (op[0].GetString())
                    {
                        case "down":
                            bool wasCompact = !ed.Active;
                            if (wasCompact) ed.Active = true;           // the view activates the clip first
                            ed.Down(op[1].Num(), op[2].Num(), W, wasCompact);
                            break;
                        case "move": ed.Move(op[1].Num(), W); break;
                        case "up": ed.Up(); break;
                        case "right": ed.RightClick(op[1].Num(), op[2].Num(), W); break;
                        case "wheel": ed.Wheel(op[1].Num(), op[2].Num(), op[3].GetBoolean(), W); break;
                        case "add": ed.AddSection(op[1].Num()); break;
                        case "zoom": ed.AddZoom(); break;
                        case "trim": ed.SetTrim(op[1].GetString() == "start"); break;
                        case "trimtext": ed.CommitTrimText(op[1].GetString() == "start", op[2].GetString()!); break;
                        case "speedtext": ed.CommitSpeedText(clip.Sections[op[1].Int()], op[2].GetString()!); break;
                        case "fit": ed.Fit(); break;
                        case "deactivate": ed.Active = false; break;
                        default: throw new InvalidOperationException(op[0].GetString());
                    }
                });
                var s = step.GetProperty("state");
                string Want(params string[] keys) => string.Join(" | ", keys.Select(k => k switch
                {
                    "sections" or "zooms" => string.Join(";", s.GetProperty(k).EnumerateArray().Select(r => string.Join(",", r.EnumerateArray().Select(v => Fmt(v.Num()))))),
                    "status" or "undo" => string.Join(";", s.GetProperty(k).Strs()),
                    _ => s.GetProperty(k).ValueKind switch
                    {
                        JsonValueKind.Number => Fmt(s.GetProperty(k).Num()),
                        JsonValueKind.Null => "None",
                        JsonValueKind.True => "True",
                        JsonValueKind.False => "False",
                        _ => s.GetProperty(k).GetString()!,
                    },
                }));
                string Got(params string[] keys) => string.Join(" | ", keys.Select(k => k switch
                {
                    "start" => Fmt(clip.Start), "end" => Fmt(clip.End), "playhead" => Fmt(ed.Playhead),
                    "view_a" => Fmt(ed.ViewA), "view_len" => Fmt(ed.ViewLen), "active" => ed.Active ? "True" : "False",
                    "drag" => ed.Drag.ToString(),
                    "sections" => string.Join(";", clip.Sections.Select(x => $"{Fmt(x.A)},{Fmt(x.B)},{Fmt(x.Speed)}")),
                    "zooms" => string.Join(";", clip.Zooms.Select(x => $"{Fmt(x.A)},{Fmt(x.B)},{Fmt(x.X)},{Fmt(x.Y)},{Fmt(x.W)}")),
                    "sel" => ed.Sel is null ? "None" : Fmt(clip.Sections.IndexOf(ed.Sel)),
                    "zsel" => ed.ZSel is null ? "None" : Fmt(clip.Zooms.IndexOf(ed.ZSel)),
                    "edited_len" => Fmt(ed.EditedLen), "level" => Fmt(ed.Level),
                    "status" => string.Join(";", status), "undo" => string.Join(";", undo), "previews" => Fmt(previews),
                    _ => throw new KeyNotFoundException(k),
                }));
                string[] keys = ["start", "end", "playhead", "view_a", "view_len", "active", "drag", "sections", "zooms",
                                 "sel", "zsel", "edited_len", "level", "status", "undo", "previews"];
                string w = Want(keys), g = Got(keys);
                if (w != g)
                {
                    var diff = keys.Where(k => Want(k) != Got(k)).Select(k => $"{k}: want {Want(k)}  got {Got(k)}");
                    m.Check(false, where + "\n    " + string.Join("\n    ", diff));
                    failed = true;
                }
                else m.Check(true, "");
                if (failed) break;                                   // later steps would only repeat the difference
            }
        }
        m.AssertNone();
    }

    [Fact]
    public void ThumbnailLayout()
    {
        var m = new Mismatches("thumbnail layout");
        foreach (var t in T.GetProperty("thumbs").EnumerateArray())
        {
            var clip = new TimelineClip("x.mp4", new MediaInfo(t.GetProperty("w").Int(), t.GetProperty("h").Int(), 30, t.GetProperty("dur").Num(), 1, 0, 0, null, null, null));
            var ed = new TimelineEditor(clip);
            ed.ForceView(t.GetProperty("view_a").Num(), t.GetProperty("view_len").Num());
            double W = t.GetProperty("W").Num();
            string where = t.GetRawText()[..80];
            m.Equal(t.GetProperty("tw").Int(), ed.ThumbWidth, where + " tw");
            int n1 = ed.WantN1(W);
            m.Equal(t.GetProperty("n1").Int(), n1, where + " n1");
            m.Equal(t.GetProperty("level").Int(), ed.Level, where + " level");
            m.Seq(t.GetProperty("need").EnumerateArray().Select(x => x.GetRawText()).ToList(),
                  ed.NeededThumbs(n1, _ => false).Select(x => x.ToString()).ToList(), where + " need");
        }
        m.AssertNone();
    }
}
