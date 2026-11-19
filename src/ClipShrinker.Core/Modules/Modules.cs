using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipShrinker.Core.Modules;

/// <summary>Switches of the built-in modules, read by the export engine, the timeline and the preview.
/// They default to on, so the engine (and the golden-fixture tests) work as before modules existed;
/// the app sets them from the Modules window.</summary>
public static class Features
{
    /// <summary>Speed up / slow motion / zoom sections and RIFE.</summary>
    public static volatile bool TimelineEffects = true;
    /// <summary>Per-clip bitrate presets with their hotkeys.</summary>
    public static volatile bool BitratePresets = true;
    /// <summary>2.7: sound files on the clips' Sound track lane.</summary>
    public static volatile bool SoundTrack = true;
}

/// <summary>The ids (folder names) of the modules that come with the app.</summary>
public static class ModuleIds
{
    public const string TimelineEffects = "timeline-effects";
    public const string BitratePresets = "bitrate-presets";
    public const string PictureAdjust = "picture-adjust";
    public const string Equalizer = "sound-equalizer";
    public const string SoundTrack = "sound-track";
    /// <summary>Dev: Claude edits the clips through the MCP helper (docs/mcp-agent-research.md).</summary>
    public const string AiAgent = "ai-agent";

    public static readonly string[] Builtin = [TimelineEffects, BitratePresets, PictureAdjust, Equalizer, SoundTrack, AiAgent];

    /// <summary>On after the first start (2.6: every built-in module, so a new user sees all features; a switch someone
    /// set before is kept in settings.json).</summary>
    public static bool DefaultOn(string id) => true;
}

/// <summary>modules\&lt;id&gt;\module.json: who made it, which version, and what it does.</summary>
public sealed record ModuleManifest(string Id, string Name, string Author, string Version, string Description, bool Builtin)
{
    public const string FileName = "module.json";
    public const string ChangelogName = "CHANGELOG.txt";

    /// <summary>2.7: moves a 2.6-or-older plugins\ folder (old) into modules\ (dst): plugin.json becomes module.json,
    /// files dst already has stay as they are, and old is deleted. True when old was there.</summary>
    public static bool MoveOldFolder(string old, string dst)
    {
        if (!Directory.Exists(old)) return false;
        try
        {
            foreach (var f in Directory.EnumerateFiles(old, "*", SearchOption.AllDirectories).ToList())
            {
                string rel = Path.GetRelativePath(old, f);
                if (Path.GetFileName(rel).Equals("plugin.json", StringComparison.OrdinalIgnoreCase))
                    rel = Path.Combine(Path.GetDirectoryName(rel) ?? "", FileName);
                string to = Path.Combine(dst, rel);
                if (File.Exists(to)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(f, to);
            }
            Directory.Delete(old, true);                           // only copies of what dst already has are left
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return true;
    }

    /// <summary>Reads a manifest; null when it is missing or not valid. A missing id falls back to the folder name.</summary>
    public static ModuleManifest? Parse(string json, string folderName)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return null;
            string S(string key, string def) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0 ? s.Trim() : def;
            bool builtin = o["builtin"] is JsonValue b && b.TryGetValue<bool>(out var bb) && bb;
            string id = S("id", folderName);
            return new ModuleManifest(id, S("name", id), S("author", "unknown"), S("version", "?"), S("description", ""), builtin);
        }
        catch (JsonException) { return null; }
    }

    public static ModuleManifest? Read(string dir)
    {
        try { return Parse(File.ReadAllText(Path.Combine(dir, FileName)), Path.GetFileName(dir)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The module's CHANGELOG.txt (null when it has none).</summary>
    public static string? ReadChangelog(string dir)
    {
        try { return File.ReadAllText(Path.Combine(dir, ChangelogName)).Trim(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Every module folder under root that has a readable module.json, sorted by name.</summary>
    public static List<(string Dir, ModuleManifest Manifest)> Scan(string root)
    {
        var res = new List<(string, ModuleManifest)>();
        if (!Directory.Exists(root)) return res;
        foreach (var d in Directory.GetDirectories(root))
            if (Read(d) is { } m) res.Add((d, m));
        return res.OrderBy(x => x.Item2.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"1.2.10" > "1.2.9"; text parts compare as text. Used to tell whether a download is newer.</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            string x = i < pa.Length ? pa[i] : "0", y = i < pb.Length ? pb[i] : "0";
            int c = int.TryParse(x, out var nx) && int.TryParse(y, out var ny) ? nx.CompareTo(ny) : string.CompareOrdinal(x, y);
            if (c != 0) return Math.Sign(c);
        }
        return 0;
    }
}
