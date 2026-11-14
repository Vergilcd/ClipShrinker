using System.Text.Json;
using System.Text.Json.Nodes;
using ClipShrinker.Core.Py;
using ClipShrinker.Core.Settings;
using static ClipShrinker.Core.Py.PyJson;

namespace ClipShrinker.Core.Project;

/// <summary>One clip in the timeline (the data part of the reference ClipRow).</summary>
public sealed class TimelineClip(string path, MediaInfo info)
{
    public string Path { get; } = path;
    public MediaInfo Info { get; } = info;
    public double Start { get; set; } = 0.0;
    public double End { get; set; } = info.Dur;
    /// <summary>Name of the bitrate preset (null = Auto).</summary>
    public string? Preset { get; set; }
    public List<SpeedSection> Sections { get; } = [];
    public List<ZoomSection> Zooms { get; } = [];

    public ExportClip ToExport() => ExportClip.FromTimeline(Path, Start, End, Info, Sections, Zooms);
}

public sealed class ProjectException(string message) : Exception(message);

public sealed record ProjectLoad(List<TimelineClip> Clips, int Active, JsonObject? Settings,
                                 List<string> Missing, List<string> Failed);

/// <summary>.clipproj reading (reference load_project, _load_clips, add_files; lines 5516-5597, 5333-5370).</summary>
public static class ProjectFile
{
    public static readonly string[] VideoExts = [".mp4", ".mov", ".mkv", ".m4v", ".avi", ".webm"];

    public static ProjectLoad Load(string path, Func<string, MediaInfo> probe, Func<string, Preset?> presetByName,
                                   Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? File.Exists;
        JsonObject data;
        try
        {
            data = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new ProjectException("not an object");
            if (data["clips"] is not JsonArray) throw new ProjectException("no clips list");
        }
        catch (Exception e) when (e is JsonException or IOException or ProjectException or UnauthorizedAccessException)
        {
            throw new ProjectException("This is not a valid Clip Shrinker project file.");
        }
        string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        var missing = new List<string>();
        var failed = new List<string>();
        var clips = new List<TimelineClip>();
        foreach (var c in data["clips"]!.AsArray())
        {
            var obj = c as JsonObject;
            string p = obj is not null && obj.TryGetPropertyValue("path", out var pv) ? PyStrPath(pv) : "";
            if (!exists(p) && obj is not null && Str(obj["rel"]) is { Length: > 0 } rel)
            {
                string alt = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, rel));
                if (exists(alt)) p = alt;
            }
            if (!exists(p))
            {
                string shown = obj is not null ? PyStr(obj.TryGetPropertyValue("path", out var x) ? x : JsonValue.Create("?")) : "?";
                missing.Add(BaseName(shown));
                continue;
            }
            // add_files([p])
            if (!VideoExts.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                failed.Add($"{BaseName(p)}: not a video file");
                continue;
            }
            MediaInfo info;
            try { info = probe(p); }
            catch (Exception e) when (e is Media.MediaException or IOException or JsonException)
            {
                string m = e.Message;
                failed.Add($"{BaseName(p)}: {(m.Length > 120 ? m[..120] : m)}");
                continue;
            }
            var row = new TimelineClip(p, info);
            ApplyClip(row, obj, presetByName);
            clips.Add(row);
        }
        var idx = data["active"];                 // isinstance(idx, int) is also true for a bool
        int active = IsIntOrBool(idx) && AsLong(idx!) >= 0 && AsLong(idx!) < clips.Count ? (int)AsLong(idx!) : 0;
        return new ProjectLoad(clips, clips.Count > 0 ? active : 0, data["settings"] as JsonObject, missing, failed);
    }

    /// <summary>c.get("path", "") as a path string (a non-string path is never an existing file).</summary>
    static string PyStrPath(JsonNode? n) => Str(n) ?? "";

    /// <summary>os.path.basename for Windows and / paths.</summary>
    public static string BaseName(string p)
    {
        int i = p.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? p[(i + 1)..] : p;
    }

    /// <summary>_load_clips for one clip: a bad value stops reading the rest of that clip (like the reference try block).</summary>
    static void ApplyClip(TimelineClip row, JsonObject? c, Func<string, Preset?> presetByName)
    {
        double dur = row.Info.Dur;
        try
        {
            if (c is null) throw new PyValueError("'str' object has no attribute 'get'");
            double a = c.TryGetPropertyValue("start", out var sv) ? Float(sv) : 0;
            double b = c.TryGetPropertyValue("end", out var ev) ? Float(ev) : dur;
            if (0 <= a && a < b && b <= dur + 0.01) { row.Start = a; row.End = Py.Py.Min(b, dur); }
            if (Str(c["preset"]) is { } name) row.Preset = presetByName(name)?.Name;
            foreach (var sd in Iter(c, "sections"))
            {
                double sa = Float(Item(sd, "a")), sb = Float(Item(sd, "b"));
                double sp = Text.ClampSpeed(Float(Item(sd, "speed")));
                if (0 <= sa && sa < sb && sb <= dur + 0.01) row.Sections.Add(new SpeedSection(sa, Py.Py.Min(sb, dur), sp));
            }
            foreach (var zd in Iter(c, "zooms"))
            {
                double za = Float(Item(zd, "a")), zb = Float(Item(zd, "b")), zw = Float(Item(zd, "w"));
                zw = Py.Py.Min(1.0, Py.Py.Max(C.ZoomMinW, zw));
                double zx = Py.Py.Min(Py.Py.Max(Float(Item(zd, "x")), 0.0), 1 - zw);
                double zy = Py.Py.Min(Py.Py.Max(Float(Item(zd, "y")), 0.0), 1 - zw);
                if (0 <= za && za < zb && zb <= dur + 0.01) row.Zooms.Add(new ZoomSection(za, Py.Py.Min(zb, dur), zx, zy, zw));
            }
        }
        catch (PyValueError) { }
    }

    /// <summary>c.get(key, []) iterated: missing = empty, a non-list raises like Python would.</summary>
    static IEnumerable<JsonNode?> Iter(JsonObject c, string key)
    {
        if (!c.TryGetPropertyValue(key, out var v)) return [];
        if (v is JsonArray arr) return arr;
        throw new PyValueError($"'{key}' is not iterable");
    }
}
