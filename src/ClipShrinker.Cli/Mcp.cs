using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClipShrinker.Core.Agent;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Cli;

/// <summary>
/// Dev (AI editing module): "clipshrinker mcp" - a local MCP server (JSON-RPC 2.0, one message per line on stdin /
/// stdout). When Clip Shrinker is open (with the AI editing module on) every tool call goes to the app, so the edits
/// show on its timeline; otherwise the tools work on a .clipproj file. See docs/mcp-agent-research.md.
/// </summary>
public static class Mcp
{
    const string Version = "0.3.0-dev";

    public static int Run(Dictionary<string, string?> opts)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;   // 1.5, not 1,5
        var raw = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);                                       // stdout is the protocol only
        var files = new FileHost(opts);
        using var link = new AgentPipeClient();
        link.Start();                                                        // (the app shows "AI agent connected")

        void Send(JsonObject o)
        {
            var b = Encoding.UTF8.GetBytes(o.ToJsonString() + "\n");
            lock (raw) { raw.Write(b); raw.Flush(); }
        }

        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? msg;
            try { msg = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { Send(Error(null, -32700, "Parse error")); continue; }
            if (msg is null || msg["id"] is not { } id) continue;           // notifications need no answer
            string method = msg["method"]?.GetValue<string>() ?? "";
            Send(method switch
            {
                "initialize" => Result(id, new JsonObject
                {
                    ["protocolVersion"] = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "clipshrinker", ["version"] = Version },
                    ["instructions"] = AgentTools.Instructions,
                }),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, new JsonObject { ["tools"] = AgentTools.List() }),
                "tools/call" => Result(id, SafeCall(msg, link, files)),
                _ => Error(id, -32601, "Method not found: " + method),
            });
        }
        return 0;
    }

    /// <summary>2.9.5: anything a call throws (also a bad "params" shape) becomes a failed result, not a dead helper.</summary>
    static JsonObject SafeCall(JsonObject msg, AgentPipeClient link, FileHost files)
    {
        try { return CallTool(msg, link, files); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return AgentTools.Content([AgentTools.Text($"The call failed ({e.GetType().Name}): {e.Message}")], isError: true);
        }
    }

    static JsonObject CallTool(JsonObject msg, AgentPipeClient link, FileHost files)
    {
        string name = msg["params"]?["name"]?.GetValue<string>() ?? "";
        var args = msg["params"]?["arguments"] as JsonObject ?? [];
        if (name == "guide") return AgentTools.Call(files, name, args);   // text only: also with an older app connected
        return link.TryCall(name, args) ?? AgentTools.Call(files, name, args);
    }

    static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    static JsonObject Result(JsonNode id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };

    /// <summary>Without the app: the clips of a .clipproj, saved after every change.</summary>
    sealed class FileHost(Dictionary<string, string?> opts) : IAgentHost
    {
        Tools? tools;
        List<TimelineClip> rows = [];
        int active;

        public Tools Tools
        {
            get
            {
                if (tools is null)
                {
                    string exeDir = AppContext.BaseDirectory;
                    var t = Tools.Locate(opts.GetValueOrDefault("--tools"), exeDir, Path.Combine(exeDir, "library"));
                    if (t.Ffmpeg is null || t.Ffprobe is null)
                        throw new AgentToolError("ffmpeg.exe / ffprobe.exe not found: start the helper with --tools <folder>.");
                    tools = t;
                }
                return tools;
            }
        }

        public bool Live => false;
        public string? ProjectPath { get; private set; }
        public IReadOnlyList<TimelineClip> Clips => rows.ToList();
        public AppState State { get; private set; } = AppState.FromSettings(null);

        public void Edit(string label, Action change)
        {
            if (ProjectPath is null) throw new AgentToolError("Open a project first (open_project or new_project), or start Clip Shrinker.");
            change();
            Save();
        }

        public void RemoveClip(TimelineClip clip) => Edit("remove clip", () => rows.Remove(clip));

        public IReadOnlyList<string> Codecs => ClipShrinker.Core.C.Codecs;
        public void Settings(string label, Action change) => Edit(label, change);
        public string? ExportFolder { get => null; set => throw new AgentToolError("The export folder is an app setting: open Clip Shrinker."); }
        /// <summary>--app = the Clip Shrinker folder (its modules\: Sounds library, speech model); default: the folder that
        /// holds the --tools "library" folder, else the helper's own folder (the tester zip puts both side by side).</summary>
        public string AppDir =>
            opts.GetValueOrDefault("--app") is { Length: > 0 } d ? d
            : opts.GetValueOrDefault("--tools") is { Length: > 0 } t && Path.GetFileName(t.TrimEnd('\\', '/')).Equals("library", StringComparison.OrdinalIgnoreCase)
                && Path.GetDirectoryName(t.TrimEnd('\\', '/')) is { } parent ? parent
            : AppContext.BaseDirectory.TrimEnd('\\', '/');
        public bool? ModuleOn(string id) => null;

        void Save() => ClipShrinker.Core.Settings.SafeFile.WriteAllText(ProjectPath!, ProjectSave.Data(rows, Math.Min(active, Math.Max(0, rows.Count - 1)), State, ProjectPath)
                                                       .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        public string OpenProject(string path)
        {
            var load = ProjectFile.Load(path, Tools.Probe, _ => null);
            (ProjectPath, rows, active) = (Path.GetFullPath(path), load.Clips, load.Active);
            State = AppState.FromSettings(null);
            State.ApplyProject(load.Settings);
            var sb = new StringBuilder();
            foreach (var m in load.Missing) sb.AppendLine("Missing (skipped): " + m);
            foreach (var f in load.Failed) sb.AppendLine("Could not read (skipped): " + f);
            sb.Append("Open it in Clip Shrinker with Ctrl+O to see it (and again after changes), or start Clip Shrinker to edit live.");
            return sb.ToString();
        }

        public string AddVideos(IReadOnlyList<string> paths)
        {
            foreach (var p in paths) if (!File.Exists(p)) throw new AgentToolError("Not found: " + p);
            Edit("add videos", () => rows.AddRange(paths.Select(p => new TimelineClip(Path.GetFullPath(p), Tools.Probe(p)))));
            return "";
        }

        public (string Text, bool Ok) Export(string? output, string? mode)
        {
            if (ProjectPath is null) throw new AgentToolError("Open a project first.");
            output ??= Path.GetDirectoryName(rows[0].Path)!;
            var o = new Dictionary<string, string?> { ["--out"] = output, ["--picture"] = null, ["--eq"] = null };
            if (opts.GetValueOrDefault("--tools") is { } td) o["--tools"] = td;
            if (mode == "join") o["--join"] = null;
            if (mode == "separate") o["--separate"] = null;
            var log = new StringWriter();
            var (oldOut, oldErr) = (Console.Out, Console.Error);
            Console.SetOut(log); Console.SetError(log);
            int code;
            try { code = Program.Export(ProjectPath, o); }
            finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
            return (log.ToString().Trim(), code == 0);
        }
    }
}
