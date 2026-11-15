using System.Text.Json.Nodes;
using ClipShrinker.Core.Settings;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Project;

/// <summary>Writing .clipproj files and the last session (reference project_data / save_project / _save_session).</summary>
public static class ProjectSave
{
    /// <summary>The project as JSON: the clips with trims, preset, speed and zoom sections, and the export settings.
    /// projectPath = where it is saved (clips also get a path relative to it, when they are on the same drive).</summary>
    public static JsonObject Data(IReadOnlyList<TimelineClip> rows, int active, AppState s, string? projectPath)
    {
        string? baseDir = projectPath is null ? null : Path.GetDirectoryName(projectPath);
        var clips = new JsonArray();
        foreach (var r in rows)
        {
            var c = new JsonObject
            {
                ["path"] = r.Path, ["start"] = RoundTo(r.Start, 3), ["end"] = RoundTo(r.End, 3), ["preset"] = r.Preset,
                ["sections"] = new JsonArray(r.Sections.OrderBy(x => x.A).Select(x => (JsonNode)new JsonObject
                    { ["a"] = RoundTo(x.A, 3), ["b"] = RoundTo(x.B, 3), ["speed"] = x.Speed }).ToArray()),
                ["zooms"] = new JsonArray(r.Zooms.OrderBy(z => z.A).Select(z => (JsonNode)new JsonObject
                    { ["a"] = RoundTo(z.A, 3), ["b"] = RoundTo(z.B, 3), ["x"] = RoundTo(z.X, 4), ["y"] = RoundTo(z.Y, 4), ["w"] = RoundTo(z.W, 4) }).ToArray()),
            };
            if (!string.IsNullOrEmpty(baseDir) && RelPath(r.Path, baseDir) is { } rel) c["rel"] = rel;
            clips.Add(c);
        }
        var data = new JsonObject
        {
            ["app"] = "Clip Shrinker", ["format"] = 1, ["clips"] = clips, ["active"] = active,
            ["settings"] = new JsonObject
            {
                ["size"] = s.Size, ["bitrate"] = s.Bitrate, ["res"] = s.Res, ["codec"] = s.Codec, ["crop"] = s.Crop,
                ["audio"] = s.Audio, ["atrk"] = s.Atrk, ["mute_mic"] = s.MuteMic,
                ["fast_audio"] = s.FastAudio, ["slow_audio"] = s.SlowAudio, ["slow_pitch"] = s.SlowPitch, ["fast_pitch"] = s.FastPitch,
                ["vhs"] = s.Vhs, ["fast_vol"] = s.FastVol, ["slow_vol"] = s.SlowVol,
                ["zoom_fx"] = s.ZoomFx, ["zoom_snd"] = s.ZoomSnd, ["zoom_len"] = s.ZoomLen, ["zoom_vol"] = s.ZoomVol,
                ["slow_interp"] = s.SlowInterp, ["hdr"] = s.Hdr, ["mode"] = s.Mode, ["trans"] = s.Trans, ["tlen"] = s.Tlen,
            },
        };
        if (s.PictureChanged)                                // (Picture adjustments plugin; older versions ignore them)
        {
            var st = (JsonObject)data["settings"]!;
            st["pic_bright"] = s.PicBright; st["pic_contrast"] = s.PicContrast; st["pic_sat"] = s.PicSat; st["pic_denoise"] = s.PicDenoise;
        }
        return data;
    }

    /// <summary>os.path.relpath on Windows: null when the paths are on different drives (Python raises ValueError).</summary>
    public static string? RelPath(string path, string baseDir)
    {
        string a = Path.GetFullPath(path), b = Path.GetFullPath(baseDir);
        if (!string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase)) return null;
        return Path.GetRelativePath(b, a);
    }

    public static void Save(string path, JsonObject data) =>
        File.WriteAllText(path, data.ToJsonString(SettingsStore.JsonOut));
}
