using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ClipShrinker.Core.Analysis;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Core.Agent;

/// <summary>Where the agent's edits go: the running app (its timeline, live, with undo) or a .clipproj file.</summary>
public interface IAgentHost
{
    Tools Tools { get; }
    /// <summary>True = the open Clip Shrinker window (edits show on its timeline at once).</summary>
    bool Live { get; }
    string? ProjectPath { get; }
    /// <summary>The clips now (a copy of the list; the clips themselves are the live objects).</summary>
    IReadOnlyList<TimelineClip> Clips { get; }
    AppState State { get; }
    /// <summary>Makes one change (one undo step in the app; saved at once to a project file).</summary>
    void Edit(string label, Action change);
    /// <summary>Opens a project (replacing what is open). Returns a note (missing files etc.) or "".</summary>
    string OpenProject(string path);
    /// <summary>Adds videos to the end of the clip list. Returns a note or "".</summary>
    string AddVideos(IReadOnlyList<string> paths);
    /// <summary>Exports with the current settings. out = file or folder (null = the app's export folder / next to the
    /// first clip); mode = "join" / "separate" / null (the saved choice). Returns the report text and success.</summary>
    (string Text, bool Ok) Export(string? output, string? mode);
    /// <summary>Takes a clip off the list (one undo step in the app).</summary>
    void RemoveClip(TimelineClip clip);
}

public sealed class AgentToolError(string m) : Exception(m);

/// <summary>
/// Dev (AI editing module, docs/mcp-agent-research.md): the tools Claude calls through MCP. The same code runs in the
/// helper (on a project file) and in the app (on its timeline, when the helper is connected to it).
/// Every call returns MCP tool-result content: { content: [...], isError }.
/// </summary>
public static class AgentTools
{
    public const string PipeName = "ClipShrinker.Agent";

    public const string Instructions = """
        Clip Shrinker edits game clips. If Clip Shrinker is open (clip_list says "the open Clip Shrinker window"), the
        tools work on the clips on its timeline and every change shows there at once, with one undo step each (Ctrl+Z).
        Otherwise open a .clipproj with open_project, or make one with new_project. add_videos adds video files.
        Clips are numbered from 1 as in the app. All times are seconds in the clip's source video.
        To find a moment the user describes: 1) contact_sheet over the kept part (every = 2 s, or 1 s for clips under a
        minute; at most 48 frames per sheet, so use larger steps or several sheets for long clips), and moments for loud
        peaks (gunshots, explosions) and scene changes; 2) frames around the candidate (every 0.25-0.5 s) to find the
        exact time; 3) set_trim / add_section, with the padding the user asks for (a few seconds = about 3 s).
        Export only when the user asks for it (export uses the app's export settings); otherwise tell them to check the
        result on the timeline (or open the project in Clip Shrinker with Ctrl+O).
        """;

    static JsonObject Schema(params (string Name, string Type, string Desc, bool Req)[] p)
    {
        var props = new JsonObject();
        foreach (var (n, t, d, _) in p)
            props[n] = t.EndsWith("[]")
                ? new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = t[..^2] }, ["description"] = d }
                : new JsonObject { ["type"] = t, ["description"] = d };
        return new JsonObject
        {
            ["type"] = "object", ["properties"] = props,
            ["required"] = new JsonArray(p.Where(x => x.Req).Select(x => (JsonNode)x.Name).ToArray()),
        };
    }

    static JsonObject Tool(string name, string desc, JsonObject schema) =>
        new() { ["name"] = name, ["description"] = desc, ["inputSchema"] = schema };

    public static JsonArray List() =>
    [
        Tool("clip_list", "The clips that are being edited (the open Clip Shrinker window, or the open project file), with " +
             "their trims and sections.", Schema()),
        Tool("open_project", "Open a .clipproj project (in the open Clip Shrinker window it replaces the clips there).",
             Schema(("path", "string", "Full path of the .clipproj file", true))),
        Tool("new_project", "Create a .clipproj with these videos (in this order) and open it.",
             Schema(("path", "string", "Full path of the new .clipproj file", true), ("videos", "string[]", "Full paths of the video files", true))),
        Tool("add_videos", "Add video files to the end of the clip list.",
             Schema(("videos", "string[]", "Full paths of the video files", true))),
        Tool("clip_info", "A clip's video details, trim, speed / zoom sections, and the picture settings.",
             Schema(("clip", "integer", "Clip number (1 = first)", true))),
        Tool("contact_sheet", "One image with many small frames of a clip, each labelled #n and its time (m:ss.s, source time). " +
             "Use it to scan a clip; at most 48 frames per sheet.",
             Schema(("clip", "integer", "Clip number", true), ("from", "number", "Start time in seconds (default: trim start)", false),
                    ("to", "number", "End time in seconds (default: trim end)", false), ("every", "number", "Seconds between frames (default 2)", false),
                    ("columns", "integer", "Frames per row (default 4)", false))),
        Tool("frames", "Larger single frames of a clip at the given times (at most 8), for a close look.",
             Schema(("clip", "integer", "Clip number", true), ("times", "number[]", "Times in seconds", true),
                    ("width", "integer", "Width in pixels (default 640)", false),
                    ("picture", "boolean", "true = with the picture adjustments (to check them)", false))),
        Tool("moments", "Times that stand out in the kept part: loud peaks (dB above the clip's usual level) and sudden " +
             "picture changes. Good candidates to check first. A long recording takes up to a minute.",
             Schema(("clip", "integer", "Clip number", true), ("max", "integer", "How many (default 12)", false))),
        Tool("set_trim", "Set the part of the clip that is kept (start and end in seconds). Sections outside it are removed.",
             Schema(("clip", "integer", "Clip number", true), ("start", "number", "Start in seconds", true), ("end", "number", "End in seconds", true))),
        Tool("add_section", "Add a speed section (speed: 2 = twice as fast, 0.5 = half speed, 0.1 to 100) or a zoom section " +
             "(x, y = top-left corner and w = width of the zoomed area, as fractions of the frame; default the middle half). " +
             "It must lie inside the trim and not overlap another section of its kind.",
             Schema(("clip", "integer", "Clip number", true), ("kind", "string", "\"speed\" or \"zoom\"", true),
                    ("from", "number", "Start in seconds", true), ("to", "number", "End in seconds", true),
                    ("speed", "number", "For kind speed", false), ("x", "number", "For zoom (0..1)", false),
                    ("y", "number", "For zoom (0..1)", false), ("w", "number", "For zoom (0.1..1)", false))),
        Tool("clear_sections", "Remove a clip's speed and / or zoom sections.",
             Schema(("clip", "integer", "Clip number", true), ("kind", "string", "\"speed\", \"zoom\" or \"all\" (default)", false))),
        Tool("remove_clip", "Take a clip off the list (the video file is not touched).",
             Schema(("clip", "integer", "Clip number", true))),
        Tool("picture_stats", "Brightness and colour of a clip (luma 0-255: mean, darkest 5 %, brightest 5 %; saturation 0-1), " +
             "with the current picture settings. Typical good footage: mean 90-140, saturation 0.25-0.45.",
             Schema(("clip", "integer", "Clip number", true))),
        Tool("set_picture", "Picture adjustments for every clip (-100..100 %, 0 = unchanged; denoise 0..100). " +
             "Check the look with frames(..., picture: true).",
             Schema(("brightness", "integer", "-100..100", false), ("contrast", "integer", "-100..100", false),
                    ("saturation", "integer", "-100..100", false), ("denoise", "integer", "0..100", false))),
        Tool("export", "Export with the app's export settings (only when the user asks). In the open app the progress shows " +
             "there. Can take minutes.",
             Schema(("out", "string", "Output .mp4 file or folder (default: the export folder, or next to the first clip)", false),
                    ("mode", "string", "\"join\" or \"separate\" when there are several clips (default: the last choice)", false))),
    ];

    /// <summary>Runs one tool. Errors the agent can fix come back as isError content, not exceptions.</summary>
    public static JsonObject Call(IAgentHost h, string name, JsonObject a)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;                 // 1.5 s, not 1,5 s
        try
        {
            PreviewCommands.Ffmpeg = h.Tools.Ffmpeg ?? PreviewCommands.Ffmpeg;
            return name switch
            {
                "clip_list" => Content([Text(ListText(h))]),
                "open_project" => Open(h, Str(a, "path")),
                "new_project" => NewProject(h, Str(a, "path"), Strs(a, "videos")),
                "add_videos" => Content([Text((h.AddVideos(Strs(a, "videos")) + "\n" + ListText(h)).Trim())]),
                "clip_info" => Content([Text(Info(h, ClipArg(h, a)))]),
                "contact_sheet" => Sheet(h, a),
                "frames" => Frames(h, a),
                "moments" => MomentsTool(h, a),
                "set_trim" => SetTrim(h, a),
                "add_section" => AddSection(h, a),
                "clear_sections" => Clear(h, a),
                "remove_clip" => Remove(h, a),
                "picture_stats" => PictureTool(h, a),
                "set_picture" => SetPicture(h, a),
                "export" => ExportTool(h, a),
                _ => throw new AgentToolError("Unknown tool: " + name),
            };
        }
        catch (AgentToolError e) { return Content([Text(e.Message)], isError: true); }
        catch (Exception e) when (e is IOException or MediaException or ProjectException or InvalidOperationException
                                      or FormatException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return Content([Text($"{name} failed: {e.Message}")], isError: true);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    // ------------------------------------------------------------------ arguments
    static string Str(JsonObject a, string k) => a[k] is JsonValue v && v.TryGetValue(out string? s) && s.Length > 0 ? s : throw new AgentToolError(k + " is required");
    static double? Num(JsonObject a, string k) => a[k] is not JsonValue v ? null
        : v.TryGetValue(out double d) ? d : v.TryGetValue(out int i) ? i : v.TryGetValue(out long l) ? l
        : v.TryGetValue(out float f) ? f : v.TryGetValue(out decimal m) ? (double)m
        : v.TryGetValue(out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : null;
    static int? Int(JsonObject a, string k) => Num(a, k) is { } d ? (int)Math.Round(d) : null;
    static List<string> Strs(JsonObject a, string k) =>
        (a[k] as JsonArray ?? throw new AgentToolError(k + " is required")).Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList();

    static TimelineClip ClipArg(IAgentHost h, JsonObject a)
    {
        var clips = h.Clips;
        if (clips.Count == 0)
            throw new AgentToolError(h.Live ? "Clip Shrinker has no clips open: add videos (add_videos) or open a project."
                                            : "No clips: open a project (open_project), make one (new_project), or start Clip Shrinker.");
        int n = Int(a, "clip") ?? 1;
        if (n < 1 || n > clips.Count) throw new AgentToolError($"There is no clip {n}: there are {clips.Count} clip(s).");
        return clips[n - 1];
    }

    static string Where(IAgentHost h) => h.Live ? "the open Clip Shrinker window" + (h.ProjectPath is { } p ? $" (project {p})" : "")
                                                : h.ProjectPath is { } f ? "project file " + f : "nothing open";

    static string ListText(IAgentHost h)
    {
        var clips = h.Clips;
        var sb = new StringBuilder($"Editing: {Where(h)}. {clips.Count} clip(s).\n");
        for (int i = 0; i < clips.Count; i++) sb.AppendLine(Summary(clips, i));
        return sb.ToString().TrimEnd();
    }

    static string Summary(IReadOnlyList<TimelineClip> clips, int i)
    {
        var r = clips[i];
        return $"Clip {i + 1}: {Path.GetFileName(r.Path)} - {r.Info.W}x{r.Info.H}, {r.Info.Fps:0.##} fps, {r.Info.Dur:0.##} s" +
               (r.Info.Trc is not null ? ", HDR" : "") + (r.Info.NAudio == 0 ? ", no sound" : "") +
               $"; kept {r.Start:0.##}-{r.End:0.##} s, {r.Sections.Count} speed / {r.Zooms.Count} zoom section(s)";
    }

    static string Info(IAgentHost h, TimelineClip r)
    {
        var clips = h.Clips;
        var sb = new StringBuilder(Summary(clips, clips.ToList().IndexOf(r)) + "\n");
        foreach (var s in r.Sections.OrderBy(s => s.A)) sb.AppendLine($"  speed x{s.Speed:0.##}: {s.A:0.##}-{s.B:0.##} s");
        foreach (var z in r.Zooms.OrderBy(z => z.A)) sb.AppendLine($"  zoom: {z.A:0.##}-{z.B:0.##} s, area x {z.X:0.##} y {z.Y:0.##} w {z.W:0.##}");
        double outLen = Timing.Duration(r.ToExport().Pieces is { Count: > 0 } p ? p : [new Piece(r.Start, r.End, 1)]);
        sb.AppendLine($"  length after editing: {outLen:0.##} s");
        var st = h.State;
        sb.Append($"  picture (all clips): brightness {st.PicBright}, contrast {st.PicContrast}, saturation {st.PicSat}, denoise {st.PicDenoise}");
        return sb.ToString();
    }

    static string Saved(IAgentHost h) => h.Live ? "Done - it shows on the timeline (Ctrl+Z undoes it). " : "Saved to the project file. ";

    // ------------------------------------------------------------------ projects
    static JsonObject Open(IAgentHost h, string path)
    {
        string note = h.OpenProject(path);
        return Content([Text((note + "\n" + ListText(h)).Trim())]);
    }

    static JsonObject NewProject(IAgentHost h, string path, List<string> videos)
    {
        if (!path.EndsWith(C.ProjectExt, StringComparison.OrdinalIgnoreCase)) path += C.ProjectExt;
        if (videos.Count == 0) throw new AgentToolError("Give at least one video.");
        var rows = new List<TimelineClip>();
        foreach (var v in videos)
        {
            if (!File.Exists(v)) throw new AgentToolError("Not found: " + v);
            rows.Add(new TimelineClip(Path.GetFullPath(v), h.Tools.Probe(v)));
        }
        path = Path.GetFullPath(path);
        ProjectSave.Save(path, ProjectSave.Data(rows, 0, AppState.FromSettings(null), path));
        return Open(h, path);
    }

    // ------------------------------------------------------------------ looking
    static JsonObject Sheet(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        double from = Math.Max(0, Num(a, "from") ?? r.Start), to = Math.Min(r.Info.Dur, Num(a, "to") ?? r.End);
        double every = Math.Max(0.1, Num(a, "every") ?? 2);
        if (to - from < 0.1) throw new AgentToolError("from must be before to.");
        if ((to - from) / every > 48.5)
            throw new AgentToolError($"That is {(int)((to - from) / every)} frames: at most 48 per sheet. Use a larger every, or several sheets (from / to).");
        var (png, times) = ClipAnalysis.ContactSheet(h.Tools.Ffmpeg!, r.Path, r.Info.W, r.Info.H, r.Info.Trc, from, to, every, Int(a, "columns") ?? 4);
        if (times.Count == 0) throw new AgentToolError("No frames could be read there.");
        return Content([Text($"{times.Count} frames, {ClipAnalysis.Clock(times[0])} to {ClipAnalysis.Clock(times[^1])}, every {every:0.##} s " +
                             $"(#n = {from:0.##} + (n-1) x {every:0.##} s)."), Image(png)]);
    }

    static JsonObject Frames(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        var times = (a["times"] as JsonArray ?? throw new AgentToolError("times is required")).Select(t => Num(new JsonObject { ["t"] = t!.DeepClone() }, "t") ?? throw new AgentToolError("times must be numbers")).Take(8).ToList();
        int w = Math.Clamp(Int(a, "width") ?? 640, 160, 1280);
        var (fw, fh) = ClipAnalysis.Fit(r.Info.W, r.Info.H, w);
        var st = h.State;
        string? pic = a["picture"] is JsonValue pv && pv.TryGetValue(out bool want) && want && st.PictureChanged
            ? Modules.PictureAdjust.Filter(st.PicBright, st.PicContrast, st.PicSat) : null;
        var content = new List<JsonNode>();
        foreach (var t in times)
        {
            double tt = Math.Clamp(t, 0, Math.Max(0, r.Info.Dur - 0.05));
            var png = ThumbnailService.GrabFrame(PreviewCommands.GrabFrame(r.Path, tt, fw, fh, null, r.Info.Trc, pic));
            content.Add(Text($"{ClipAnalysis.Clock(tt)} ({tt:0.##} s)" + (png is null ? ": could not be read" : "")));
            if (png is not null) content.Add(Image(png));
        }
        return Content(content);
    }

    static JsonObject MomentsTool(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        var list = ClipAnalysis.Moments(h.Tools.Ffmpeg!, r.Path, r.Info.NAudio > 0, r.Start, r.End, CudaOk(h.Tools), Int(a, "max") ?? 12);
        if (list.Count == 0) return Content([Text("Nothing stands out in the kept part.")]);
        return Content([Text(string.Join("\n", list.Select(m => $"{ClipAnalysis.Clock(m.Time)} ({m.Time:0.#} s): {m.Kind}, " +
                             (m.Kind == "loud" ? $"+{m.Strength:0.#} dB" : $"x{m.Strength:0.#} usual motion"))))]);
    }

    static bool? cuda;
    static bool CudaOk(Tools t) => cuda ??= t.CudaWorks();

    static JsonObject PictureTool(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        var s = ClipAnalysis.Picture(h.Tools.Ffmpeg!, r.Path, r.Info.Trc, r.Start, r.End);
        var st = h.State;
        return Content([Text($"From {s.Frames} sample frames{(r.Info.Trc is not null ? " (HDR, measured after the conversion to SDR)" : "")}: " +
                             $"mean luma {s.MeanLuma}, darkest 5 % {s.DarkLuma}, brightest 5 % {s.BrightLuma}, saturation {s.MeanSat}. " +
                             $"Current picture settings: brightness {st.PicBright}, contrast {st.PicContrast}, saturation {st.PicSat}, denoise {st.PicDenoise}.")]);
    }

    // ------------------------------------------------------------------ editing
    static JsonObject SetTrim(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        double s = Math.Clamp(Num(a, "start") ?? r.Start, 0, r.Info.Dur), e = Math.Clamp(Num(a, "end") ?? r.End, 0, r.Info.Dur);
        if (e - s < C.MinSection) throw new AgentToolError("The kept part must be at least 0.2 s long, with start before end.");
        int removed = 0;
        h.Edit("trim", () =>
        {
            (r.Start, r.End) = (s, e);
            int before = r.Sections.Count + r.Zooms.Count;
            r.Sections.RemoveAll(x => x.B <= s + C.MinSection || x.A >= e - C.MinSection);
            r.Zooms.RemoveAll(x => x.B <= s + C.MinSection || x.A >= e - C.MinSection);
            foreach (var x in r.Sections) (x.A, x.B) = (Math.Max(x.A, s), Math.Min(x.B, e));
            foreach (var x in r.Zooms) (x.A, x.B) = (Math.Max(x.A, s), Math.Min(x.B, e));
            removed = before - r.Sections.Count - r.Zooms.Count;
        });
        return Content([Text(Saved(h) + Info(h, r) + (removed > 0 ? $"\n{removed} section(s) outside the trim were removed." : ""))]);
    }

    static JsonObject AddSection(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        string kind = Str(a, "kind").ToLowerInvariant();
        double from = Math.Max(r.Start, Num(a, "from") ?? throw new AgentToolError("from is required"));
        double to = Math.Min(r.End, Num(a, "to") ?? throw new AgentToolError("to is required"));
        if (to - from < C.MinSection) throw new AgentToolError($"The section must be at least {C.MinSection} s long and inside the trim ({r.Start:0.##}-{r.End:0.##} s).");
        if (kind == "speed")
        {
            double sp = Num(a, "speed") ?? throw new AgentToolError("speed is required for a speed section");
            if (sp < C.SpeedMin || sp > C.SpeedMax || Math.Abs(sp - 1) < 1e-6) throw new AgentToolError("speed must be 0.1 to 100 and not 1.");
            if (r.Sections.FirstOrDefault(x => x.A < to - 0.001 && from < x.B - 0.001) is { } o)
                throw new AgentToolError($"It overlaps the speed section {o.A:0.##}-{o.B:0.##} s. Remove or shorten that first (clear_sections).");
            h.Edit("speed section", () => r.Sections.Add(new SpeedSection(from, to, sp)));
        }
        else if (kind == "zoom")
        {
            double w = Math.Clamp(Num(a, "w") ?? 0.5, C.ZoomMinW, 1);
            double x = Math.Clamp(Num(a, "x") ?? (1 - w) / 2, 0, 1 - w), y = Math.Clamp(Num(a, "y") ?? (1 - w) / 2, 0, 1 - w);
            if (r.Zooms.FirstOrDefault(z => z.A < to - 0.001 && from < z.B - 0.001) is { } o)
                throw new AgentToolError($"It overlaps the zoom section {o.A:0.##}-{o.B:0.##} s.");
            h.Edit("zoom section", () => r.Zooms.Add(new ZoomSection(from, to, x, y, w)));
        }
        else throw new AgentToolError("kind must be \"speed\" or \"zoom\".");
        return Content([Text(Saved(h) + Info(h, r))]);
    }

    static JsonObject Clear(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        string kind = a["kind"] is JsonValue v && v.TryGetValue(out string? k) ? k.ToLowerInvariant() : "all";
        h.Edit("remove sections", () =>
        {
            if (kind is "speed" or "all") r.Sections.Clear();
            if (kind is "zoom" or "all") r.Zooms.Clear();
        });
        return Content([Text(Saved(h) + Info(h, r))]);
    }

    static JsonObject Remove(IAgentHost h, JsonObject a)
    {
        h.RemoveClip(ClipArg(h, a));
        return Content([Text(Saved(h) + ListText(h))]);
    }

    static JsonObject SetPicture(IAgentHost h, JsonObject a)
    {
        var st = h.State;
        h.Edit("picture", () =>
        {
            if (Int(a, "brightness") is { } b) st.PicBright = Modules.PictureAdjust.Clamp(b);
            if (Int(a, "contrast") is { } c) st.PicContrast = Modules.PictureAdjust.Clamp(c);
            if (Int(a, "saturation") is { } s) st.PicSat = Modules.PictureAdjust.Clamp(s);
            if (Int(a, "denoise") is { } d) st.PicDenoise = Modules.PictureAdjust.ClampDenoise(d);
        });
        return Content([Text(Saved(h) + $"Picture: brightness {st.PicBright}, contrast {st.PicContrast}, saturation {st.PicSat}, denoise {st.PicDenoise}" +
                             " (used when the Picture adjustments module is on).")]);
    }

    static JsonObject ExportTool(IAgentHost h, JsonObject a)
    {
        if (h.Clips.Count == 0) throw new AgentToolError("There are no clips to export.");
        string? output = a["out"] is JsonValue v && v.TryGetValue(out string? o) && o.Length > 0 ? o : null;
        string? mode = a["mode"] is JsonValue m && m.TryGetValue(out string? md) ? md : null;
        var (text, ok) = h.Export(output, mode);
        return Content([Text(text)], isError: !ok);
    }

    // ------------------------------------------------------------------ content
    public static JsonObject Text(string t) => new() { ["type"] = "text", ["text"] = t };
    public static JsonObject Image(byte[] png) => new() { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" };
    public static JsonObject Content(IEnumerable<JsonNode> items, bool isError = false) =>
        new() { ["content"] = new JsonArray(items.ToArray()), ["isError"] = isError };
}
