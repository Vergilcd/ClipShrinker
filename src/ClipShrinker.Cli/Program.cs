using System.Text.Json;
using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Export;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Settings;

namespace ClipShrinker.Cli;

/// <summary>
/// Runs an export from a .clipproj without the UI, the same way the 1.0.8 Export button does.
///   clipshrinker export project.clipproj --out file.mp4|folder [--join|--separate] [--settings settings.json]
///                       [--presets presets.json] [--tools dir] [--rife dir] [--no-rife] [--log-commands file.json]
///   clipshrinker probe video.mp4 [--tools dir]
/// </summary>
public static class Program
{
    const string Usage = """
        Usage:
          clipshrinker export <project.clipproj> --out <file.mp4 | folder> [options]
          clipshrinker probe <video> [--tools <dir>]

        Options for export:
          --join | --separate     how to export several clips (default: the project's "mode")
          --settings <file>       settings.json with the app's saved defaults (default: none = app defaults)
          --presets <file>        presets.json for per-clip bitrate presets
          --tools <dir>           folder with ffmpeg.exe / ffprobe.exe (default: next to this exe, then PATH)
          --rife <dir>            folder with rife-ncnn-vulkan.exe and a rife-v4 model (default: <tools>\rife)
          --no-rife               treat RIFE as not installed
          --log-commands <file>   write every command that ran to a JSON file
        """;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 2 && args[0] == "export") return Export(args[1], Options(args[2..]));
            if (args.Length >= 2 && args[0] == "probe") return ProbeCmd(args[1], Options(args[2..]));
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
        }
        Console.Error.WriteLine(Usage);
        return 2;
    }

    sealed class UsageException(string m) : Exception(m);

    static Dictionary<string, string?> Options(string[] a)
    {
        var o = new Dictionary<string, string?>();
        string[] flags = ["--join", "--separate", "--no-rife"];
        for (int i = 0; i < a.Length; i++)
        {
            if (flags.Contains(a[i])) o[a[i]] = null;
            else if (a[i].StartsWith("--") && i + 1 < a.Length) o[a[i]] = a[++i];
            else throw new UsageException("Unknown or incomplete option: " + a[i]);
        }
        return o;
    }

    static Tools LocateTools(Dictionary<string, string?> o)
    {
        string exeDir = AppContext.BaseDirectory;
        var t = Tools.Locate(o.GetValueOrDefault("--tools"), exeDir, Path.Combine(exeDir, "library"));
        if (t.Ffmpeg is null || t.Ffprobe is null)
            throw new UsageException("Could not find ffmpeg.exe / ffprobe.exe. Use --tools <folder> or add them to PATH.");
        return t;
    }

    static int ProbeCmd(string path, Dictionary<string, string?> o)
    {
        var info = LocateTools(o).Probe(path);
        Console.WriteLine(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    static int Export(string projectPath, Dictionary<string, string?> o)
    {
        if (!o.TryGetValue("--out", out var outArg) || string.IsNullOrEmpty(outArg)) throw new UsageException("--out is required.");
        var tools = LocateTools(o);
        if (!tools.HasNvenc())
            Console.Error.WriteLine("Warning: this ffmpeg build has no NVENC encoder. Update your NVIDIA driver or use a full ffmpeg build.");

        var state = AppState.FromSettings(o.GetValueOrDefault("--settings") is { } sp ? SettingsStore.Load(sp) : null);
        var presets = o.GetValueOrDefault("--presets") is { } pp ? PresetStore.Load(pp) : [];
        Preset? PresetByName(string name) => presets.FirstOrDefault(p => p.Name == name);

        ProjectLoad proj;
        try { proj = ProjectFile.Load(projectPath, tools.Probe, PresetByName); }
        catch (ProjectException e) { Console.Error.WriteLine(e.Message); return 1; }
        state.ApplyProject(proj.Settings);
        foreach (var m in proj.Missing) Console.Error.WriteLine($"Not found, skipped: {m}");
        foreach (var f in proj.Failed) Console.Error.WriteLine($"Skipped: {f}");
        var rows = proj.Clips;
        if (rows.Count == 0) { Console.Error.WriteLine("No clips: add at least one video first."); return 1; }

        var clips = ExportPlanner.PrepareClips(state, rows);
        var inputs = ExportPlanner.InputSet(clips);
        bool join = false;
        if (clips.Count > 1)
            join = o.ContainsKey("--join") || (!o.ContainsKey("--separate") && state.Mode == "join");

        var st = state.ToExportSettings(join, out var error);
        if (st is null) { Console.Error.WriteLine(error); return 1; }

        List<ExportJob> jobs;
        if (join || clips.Count == 1)
        {
            string output = Directory.Exists(outArg)
                ? ExportPlanner.AutoPath(state, outArg, ExportPlanner.Stem(clips[0].Path), inputs) : Path.GetFullPath(outArg);
            if (inputs.Contains(output.ToLowerInvariant())) { Console.Error.WriteLine("Choose a different output file name."); return 1; }
            jobs = [ExportPlanner.OneFile(state, st, rows, clips, output, join, PresetByName)];
        }
        else
        {
            Directory.CreateDirectory(outArg);
            jobs = ExportPlanner.Separate(state, st, rows, clips, outArg, PresetByName);
        }

        var console = new ConsoleStatus();
        var runner = new ProcessRunner(console.Progress, console.Status);
        RifeInstall? rife = o.ContainsKey("--no-rife") ? null
            : RifeInstall.Find(o.GetValueOrDefault("--rife") ?? Path.Combine(Path.GetDirectoryName(tools.Ffmpeg!)!, "rife"));
        var enc = new Encoder(runner, new RealFileSystem(), console.Progress, console.Status, rife, runner, Console.Error.WriteLine) { Ffmpeg = tools.Ffmpeg! };
        bool stopping = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping = true; enc.Cancel(); };

        var done = new List<(string Out, long Size)>();
        var errors = new List<string>();
        var log = new JsonArray();
        var t0 = DateTime.Now;
        string? current = null;
        int code = 0;
        try
        {
            for (int k = 0; k < jobs.Count; k++)
            {
                var (output, jclips, jst, _) = jobs[k];
                current = output;
                string tag = jobs.Count > 1 ? $"[{k + 1}/{jobs.Count}] " : "";
                int first = runner.Started.Count;
                try
                {
                    long size = enc.Encode(output, jclips, jst, 100.0 * k / jobs.Count, 100.0 / jobs.Count, tag);
                    done.Add((output, size));
                }
                catch (ExportException e)
                {
                    errors.Add($"{Path.GetFileName(output)}: {e.Message}");
                    try { File.Delete(output); } catch (IOException) { }
                }
                finally
                {
                    log.Add(new JsonObject
                    {
                        ["out"] = output,
                        ["commands"] = new JsonArray(runner.Started.Skip(first)
                            .Select(c => (JsonNode)new JsonArray(c.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())).ToArray()),
                    });
                }
            }
            console.End();
            var (message, _) = ExportPlanner.Report(jobs, st, done, errors);
            Console.WriteLine(message.Length > 0 ? message : "Nothing was saved");
            if (errors.Count > 0) code = 1;
            Console.WriteLine($"Took {Text.FmtElapsed((DateTime.Now - t0).TotalSeconds)}");
        }
        catch (ExportCancelledException)
        {
            console.End();
            Console.Error.WriteLine("The export was stopped - the unfinished file was deleted");
            if (current is not null) try { File.Delete(current); } catch (IOException) { }
            code = 130;
        }
        if (o.GetValueOrDefault("--log-commands") is { } lp)
            File.WriteAllText(lp, new JsonObject { ["jobs"] = log }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return stopping ? 130 : code;
    }

    /// <summary>Status line on the console: label changes get their own line, percentages overwrite the line.</summary>
    sealed class ConsoleStatus
    {
        readonly object gate = new();
        string lastLabel = "";
        bool open;
        readonly bool interactive = !Console.IsErrorRedirected;

        public void Progress(double pct) { }

        public void Status(string text)
        {
            lock (gate)
            {
                int k = text.LastIndexOf(": ", StringComparison.Ordinal);
                bool isPct = k > 0 && text.EndsWith('%');
                string label = isPct ? text[..k] : text;
                if (label != lastLabel)
                {
                    if (open) Console.Error.WriteLine();
                    lastLabel = label;
                    open = false;
                    if (!isPct) { Console.Error.WriteLine(text); return; }
                }
                if (isPct && interactive) { Console.Error.Write("\r" + text.PadRight(70)); open = true; }
            }
        }

        public void End()
        {
            lock (gate) { if (open) Console.Error.WriteLine(); open = false; }
        }
    }
}
