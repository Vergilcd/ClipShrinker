using System.IO;
using System.Windows;
using ClipShrinker.Core;
using ClipShrinker.Core.Agent;
using ClipShrinker.Core.Export;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Modules;
using ClipShrinker.Core.Project;

namespace ClipShrinker.App.ViewModels;

/// <summary>Dev (AI editing module): Claude's MCP helper connects to the open window over a local pipe and edits the
/// clips on the timeline (each change one undo step). The header shows whether a helper is connected.</summary>
public sealed partial class MainViewModel
{
    public bool AgentModuleOn => ModuleOn(ModuleIds.AiAgent);

    AgentPipeServer? agentServer;
    int agentConnections;

    public bool AgentConnected => agentConnections > 0;
    public string AgentText => AgentConnected ? "AI agent connected" : "AI agent not connected";
    public string AgentTip => AgentConnected
        ? "Claude is connected (AI editing module): what it changes shows on the timeline, and Ctrl+Z undoes each change."
        : "No Claude session is connected. Click Modules > AI editing > Connect to Claude Code once, then start a new " +
          "Claude Code session; it connects by itself while Clip Shrinker is open.";

    /// <summary>Starts or stops listening for the helper when the module is switched.</summary>
    void ApplyAgent()
    {
        if (AgentModuleOn && agentServer is null)
        {
            agentServer = new AgentPipeServer(() => new AppAgentHost(this));
            agentServer.ConnectionsChanged += n => Ui(() =>
            {
                bool was = AgentConnected;
                agentConnections = n;
                OnPropertyChanged(nameof(AgentConnected));
                OnPropertyChanged(nameof(AgentText));
                OnPropertyChanged(nameof(AgentTip));
                if (was != AgentConnected) Dialogs.Toast(AgentConnected ? "Claude connected (AI editing)" : "Claude disconnected");
            });
            agentServer.Start();
        }
        else if (!AgentModuleOn && agentServer is not null)
        {
            agentServer.Dispose();
            agentServer = null;
            agentConnections = 0;
        }
        OnPropertyChanged(nameof(AgentModuleOn));
        OnPropertyChanged(nameof(AgentConnected));
        OnPropertyChanged(nameof(AgentText));
        OnPropertyChanged(nameof(AgentTip));
    }

    /// <summary>After an agent change: every clip redraws, the summary and the preview follow.</summary>
    void AgentChanged()
    {
        foreach (var c in Clips)
        {
            c.Editor.Sel = null;
            c.Editor.ZSel = null;
            c.Editor.Playhead = Math.Min(Math.Max(c.Editor.Playhead, c.Row.Start), c.Row.End);
            c.NotifyChanged();
        }
        foreach (var n in new[] { nameof(PicBright), nameof(PicContrast), nameof(PicSat), nameof(PicDenoise), nameof(PicTemp), nameof(PicTint) })
        { OnPropertyChanged(n); OnPropertyChanged(n + "Text"); }
        OnPropertyChanged(nameof(PictureChanged));
        PreviewSettingsChanged();
        Refresh();
        if (ActiveClip is { } a) RequestPreview(a, a.Editor.Playhead);
    }

    /// <summary>The Export button without its questions: the agent gives the file / folder and join or separate.
    /// Null = started; otherwise the reason it did not.</summary>
    string? StartAgentExport(string? output, string? mode)
    {
        if (Exporting) return "An export is already running.";
        StopPlayback();
        if (S.ToExportSettings(false, out var error) is null) return error;
        var rows = Clips.Select(c => c.Row).ToList();
        var clips = ExportPlanner.PrepareClips(S, rows);
        var inputs = ExportPlanner.InputSet(clips);
        if (mode is "join" or "separate") S.Mode = mode;
        bool join = clips.Count > 1 && S.Mode == "join";
        var st = S.ToExportSettings(join, out _)!;
        string firstDir = Path.GetDirectoryName(clips[0].Path)!;
        bool isDir = output is not null && (Directory.Exists(output) || !Path.HasExtension(output));
        List<ExportJob> jobs;
        if (join || clips.Count == 1)
        {
            string target;
            if (output is null || isDir)
            {
                string dir = output ?? InitDir() ?? firstDir;
                Directory.CreateDirectory(dir);
                var names = ExportPlanner.OneFileName(S, st, rows, clips, join, PresetByName);
                target = ExportPlanner.AutoPath(S, dir, ExportPlanner.Stem(clips[0].Path), inputs, null, names);
            }
            else target = Path.GetFullPath(output);
            if (inputs.Contains(target.ToLowerInvariant())) return "That is one of the source videos: choose another file name.";
            jobs = [ExportPlanner.OneFile(S, st, rows, clips, target, join, PresetByName)];
        }
        else
        {
            string folder = output is not null && isDir ? output : InitDir() ?? firstDir;
            Directory.CreateDirectory(folder);
            jobs = ExportPlanner.Separate(S, st, rows, clips, folder, PresetByName);
        }
        RunExport(jobs, st);
        return null;
    }

    /// <summary>The open window as the agent's host. Called on the pipe's thread; everything that touches the clips
    /// runs on the UI thread.</summary>
    sealed class AppAgentHost(MainViewModel vm) : IAgentHost
    {
        static T Ui<T>(Func<T> f) => Application.Current.Dispatcher.Invoke(f);
        static void UiDo(Action a) => Application.Current.Dispatcher.Invoke(a);

        public Tools Tools => vm.Tools;
        public bool Live => true;
        public string? ProjectPath => Ui(() => vm.ProjectPath);
        public IReadOnlyList<TimelineClip> Clips => Ui(() => vm.Clips.Select(c => c.Row).ToList());
        public AppState State => vm.S;
        public IReadOnlyList<string> Codecs => Ui(() => vm.Codecs);
        public string AppDir => AppPaths.ExeDir;
        public bool? ModuleOn(string id) => Ui(() => vm.ModuleOn(id));

        public string? ExportFolder
        {
            get => Ui(() => vm.SaveDir);
            set => UiDo(() => vm.SaveDir = value ?? "");
        }

        /// <summary>App-wide settings: no undo step (undo covers the clips); every binding re-reads its value.</summary>
        public void Settings(string label, Action change) => UiDo(() =>
        {
            if (vm.Exporting) throw new AgentToolError("Clip Shrinker is exporting: wait until it is done.");
            vm.StopPlayback();
            change();
            vm.AgentChanged();
            vm.OnPropertyChanged(string.Empty);
        });

        public void Edit(string label, Action change) => UiDo(() =>
        {
            if (vm.Exporting) throw new AgentToolError("Clip Shrinker is exporting: wait until it is done.");
            vm.StopPlayback();
            vm.PushUndo("Claude: " + label);
            change();
            vm.AgentChanged();
        });

        public void RemoveClip(TimelineClip clip) => UiDo(() =>
        {
            if (vm.Clips.FirstOrDefault(c => ReferenceEquals(c.Row, clip)) is { } item) vm.Remove(item);
        });

        public string OpenProject(string path)
        {
            if (!File.Exists(path)) throw new AgentToolError("Not found: " + path);
            Ui(() => vm.OpenProject(path, ask: false)).Wait();
            return "";
        }

        public string AddVideos(IReadOnlyList<string> paths)
        {
            foreach (var p in paths) if (!File.Exists(p)) throw new AgentToolError("Not found: " + p);
            Ui(() => vm.AddFiles(paths)).Wait();
            return "";
        }

        public (string Text, bool Ok) Export(string? output, string? mode)
        {
            if (Ui(() => vm.StartAgentExport(output, mode)) is { } why) return (why, false);
            while (Ui(() => vm.Exporting)) Thread.Sleep(500);              // the progress shows in the app meanwhile
            return Ui(() => ($"{vm.ProgressText}. {vm.ExportMessage}" + (vm.OutPath is { } o ? $" Saved: {o}" : ""),
                             vm.ProgressLook == ProgressLook.Done));
        }
    }
}
