using ClipShrinker.Core.Settings;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Project;

/// <summary>One output file: its clips and the settings it is exported with.</summary>
public sealed record ExportJob(string Out, IReadOnlyList<ExportClip> Clips, ExportSettings Settings, bool HasOverride);

/// <summary>What the Export button does before and after the encoder runs (reference start_export, _export_worker).</summary>
public static class ExportPlanner
{
    /// <summary>The clips as the encoder gets them; "Mute microphone" keeps only the first audio track, and the
    /// Picture adjustments plugin adds its filter to every clip (no denoise on clips that stay HDR).</summary>
    public static List<ExportClip> PrepareClips(AppState state, IEnumerable<TimelineClip> rows)
    {
        return rows.Select(r => r.ToExport())
                   .Select(c => state.MuteMic ? c with { NAudio = Math.Min(c.NAudio ?? 1, 1) } : c)
                   .Select(c => state.PictureFilter(hdrKept: !state.Hdr && !string.IsNullOrEmpty(c.Trc)) is { } pic ? c with { PluginVf = [pic] } : c)
                   .ToList();
    }

    public static HashSet<string> InputSet(IEnumerable<ExportClip> clips) =>
        clips.Select(c => Path.GetFullPath(c.Path).ToLowerInvariant()).ToHashSet();

    public static string Stem(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>folder/stem_tag.mp4, made unique (name (2).mp4 ...) so nothing is overwritten.</summary>
    public static string AutoPath(AppState state, string folder, string stem, ICollection<string> taken, Func<string, bool>? exists = null,
                                  NameContext? ctx = null)
    {
        exists ??= File.Exists;
        string bas = Path.Combine(folder, state.OutStem(stem, ctx));
        string p = bas + ".mp4";
        for (int i = 2; exists(p) || taken.Contains(p.ToLowerInvariant()); i++) p = $"{bas} ({I(i)}).mp4";
        return p;
    }

    /// <summary>The name fields of the joined video, or of the single clip (with its own bitrate preset).</summary>
    public static NameContext OneFileName(AppState state, ExportSettings st, IReadOnlyList<TimelineClip> rows, IReadOnlyList<ExportClip> clips,
                                          bool join, Func<string, Preset?> presetByName)
    {
        var over = !join && rows[0].ActivePreset is { } n ? presetByName(n) : null;
        return new NameContext(DateTime.Now, over?.Name, clips, join ? st : state.ClipOverride(st, over));
    }

    /// <summary>Joined video, or the single clip (which keeps its own bitrate preset).</summary>
    public static ExportJob OneFile(AppState state, ExportSettings st, IReadOnlyList<TimelineClip> rows, IReadOnlyList<ExportClip> clips,
                                    string output, bool join, Func<string, Preset?> presetByName)
    {
        if (join) return new ExportJob(output, clips, st, false);
        var over = rows[0].ActivePreset is { } n ? presetByName(n) : null;
        return new ExportJob(output, clips, state.ClipOverride(st, over), over is not null);
    }

    /// <summary>Each clip as its own file in folder (with its bitrate preset).</summary>
    public static List<ExportJob> Separate(AppState state, ExportSettings st, IReadOnlyList<TimelineClip> rows,
                                           IReadOnlyList<ExportClip> clips, string folder, Func<string, Preset?> presetByName,
                                           Func<string, bool>? exists = null)
    {
        var jobs = new List<ExportJob>();
        var used = new HashSet<string>();
        var now = DateTime.Now;                              // one time for all files of this export
        for (int i = 0; i < clips.Count; i++)
        {
            var over = rows[i].ActivePreset is { } n ? presetByName(n) : null;
            var cst = state.ClipOverride(st, over);
            string p = AutoPath(state, folder, Stem(clips[i].Path), used, exists, new NameContext(now, over?.Name, [clips[i]], cst));
            used.Add(p.ToLowerInvariant());
            jobs.Add(new ExportJob(p, [clips[i]], cst, over is not null));
        }
        return jobs;
    }

    /// <summary>The result text of an export: (message box text, one-line status).</summary>
    public static (string Message, string Line) Report(IReadOnlyList<ExportJob> jobs, ExportSettings st,
                                                      IReadOnlyList<(string Out, long Size)> done, IReadOnlyList<string> errors)
    {
        string limit = jobs.Any(j => j.HasOverride) ? "per-clip bitrates"
            : st.BitrateMbps is { } b && b != 0 ? $"video bitrate {G(b)} Mbps" : $"limit {G(st.TargetMb ?? 0)} MB each";
        var lines = done.Select(d => $"{Path.GetFileName(d.Out)}  -  {F(d.Size / 1048576.0, 2)} MB");
        string msg = done.Count > 0 ? $"Saved {I(done.Count)} file(s) ({limit}):\n\n" + string.Join("\n", lines) : "";
        if (errors.Count > 0) msg += (msg.Length > 0 ? "\n\n" : "") + "Problems:\n" + string.Join("\n", errors);
        string info = string.Join(", ", done.Take(3).Select(d => $"{Path.GetFileName(d.Out)} ({F(d.Size / 1048576.0, 2)} MB)"));
        if (done.Count > 3) info += $" + {I(done.Count - 3)} more";
        string line = (done.Count > 0 ? $"{I(done.Count)} saved ({limit}): {info}" : "Nothing was saved")
                      + (errors.Count > 0 ? $"  |  {I(errors.Count)} failed: {errors[0]}" : "");
        return (msg, line);
    }
}
