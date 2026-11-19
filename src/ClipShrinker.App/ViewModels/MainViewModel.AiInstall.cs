using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using ClipShrinker.Core.Modules;
using CommunityToolkit.Mvvm.Input;

namespace ClipShrinker.App.ViewModels;

/// <summary>2.8: the AI editing module is downloaded from the GitHub release "ai-editing" (Core.Modules.AiModule):
/// Install, Update, Uninstall and Connect to Claude Code on its card in the Modules window.</summary>
public sealed partial class MainViewModel
{
    ModuleItem? AiItem => Modules.FirstOrDefault(m => m.Id == ModuleIds.AiAgent);
    AiManifest? aiManifest;

    static string Mb(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{Math.Max(1, (bytes + (1L << 19)) >> 20)} MB";

    /// <summary>Start-up: leftovers of an update go; when installed, the manifest is read at most once a day in the
    /// background and the card offers the update. When not installed, the card learns the download size.</summary>
    void InitAiModule()
    {
        AiModule.CleanOld(AppPaths.ExeDir);
        bool installed = AiItem?.Installed == true;
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (installed && Core.Py.PyJson.Str(settings["ai_update_checked"]) == today) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var m = await AiModule.FetchManifest();
                Ui(() =>
                {
                    aiManifest = m;
                    if (installed) settings["ai_update_checked"] = today;
                    ShowAiUpdate();
                });
            }
            catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or InvalidDataException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"ai-editing manifest: {e.Message}");
            }
        });
    }

    void ShowAiUpdate()
    {
        if (AiItem is not { } p || aiManifest is not { } m) return;
        if (p.Installed)
        {
            var ups = AiModule.Updates(AppPaths.ExeDir, m);
            p.Update = ups.Count == 0 ? "" : $"v{m.ModuleVersion}, {Mb(ups.Sum(u => u.Size))}";
        }
        else
        {
            var need = AiModule.Missing(AppPaths.ExeDir, m, withOptional: false);
            var opt = AiModule.Missing(AppPaths.ExeDir, m, withOptional: true).Except(need).ToList();
            p.InstallSize = Mb(need.Sum(x => x.Size)) + (opt.Count > 0 ? $" + {Mb(opt.Sum(x => x.Size))} for speech to text" : "");
        }
    }

    async Task<AiManifest?> GetAiManifest(ModuleItem p)
    {
        p.Busy = "Reading the AI editing download list from GitHub...";
        try { return aiManifest = await AiModule.FetchManifest(); }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or InvalidDataException or UnauthorizedAccessException)
        {
            Dialogs.Error("AI editing", $"The download list could not be read ({e.Message}).\n\nFrom: {AiModule.Source}");
            return null;
        }
        finally { p.Busy = ""; }
    }

    [RelayCommand]
    async Task InstallAi(ModuleItem p)
    {
        if (!p.CanInstall || await GetAiManifest(p) is not { } m) return;
        var need = AiModule.Missing(AppPaths.ExeDir, m, withOptional: false);
        var opt = AiModule.Missing(AppPaths.ExeDir, m, withOptional: true).Except(need).ToList();
        var parts = need;
        string list = string.Join("\n", need.Select(x => $"  •  {x.Name} ({Mb(x.Size)})"));
        if (opt.Count > 0)
        {
            string optList = string.Join("\n", opt.Select(x => $"  •  {x.Name} ({Mb(x.Size)})"));
            int c = Dialogs.Choice("Install AI editing",
                $"Clip Shrinker downloads these parts from GitHub into\n{AiModule.Dir(AppPaths.ExeDir)}\n\n{list}\n\n" +
                $"For speech to text (finding moments by what is said) it also needs:\n{optList}\n\n" +
                "Without it, the speech model is downloaded the first time Claude asks for speech.",
                [$"Install all ({Mb(need.Concat(opt).Sum(x => x.Size))})", $"Without speech ({Mb(need.Sum(x => x.Size))})", "Cancel"], 0, 2);
            if (c == 2) return;
            if (c == 0) parts = [.. need, .. opt];
        }
        else if (!Dialogs.YesNo("Install AI editing", $"Clip Shrinker downloads these parts from GitHub into\n{AiModule.Dir(AppPaths.ExeDir)}\n\n{list}\n\nInstall?"))
            return;
        if (!await RunAiDownload(p, m, parts, "installed")) return;

        if (ModuleManifest.Read(p.Dir) is null) WriteBuiltinFiles(p.Id);           // (module.json was deleted)
        if (ModuleManifest.Read(p.Dir) is { } mf) p.Fill(mf, ModuleManifest.ReadChangelog(p.Dir));
        p.Version = AiModule.InstalledVersion(AppPaths.ExeDir);
        p.Installed = true;
        p.Update = "";
        ShowAiUpdate();
        // switching on shows the third-party data notice (OnModuleToggled)
        p.Enabled = true;
        if (p.Enabled) await ConnectAi(p, ask: true);
    }

    [RelayCommand]
    async Task UpdateAi(ModuleItem p)
    {
        if (!p.CanUpdate || await GetAiManifest(p) is not { } m) return;
        var ups = AiModule.Updates(AppPaths.ExeDir, m);
        if (ups.Count == 0) { p.Update = ""; Dialogs.Toast("AI editing is up to date"); return; }
        string list = string.Join("\n", ups.Select(x => $"  •  {x.Name} {x.Version} ({Mb(x.Size)})"));
        if (!Dialogs.YesNo("Update AI editing", $"Download the update (v{m.ModuleVersion})?\n\n{list}\n\n" +
                                                "A running Claude session keeps the old helper until it is restarted.")) return;
        if (!await RunAiDownload(p, m, ups, "updated")) return;
        p.Version = AiModule.InstalledVersion(AppPaths.ExeDir);
        p.Update = "";
    }

    async Task<bool> RunAiDownload(ModuleItem p, AiManifest m, IReadOnlyList<AiPart> parts, string done)
    {
        p.Busy = "Starting the download...";
        try
        {
            await Task.Run(() => AiModule.Install(AppPaths.ExeDir, m, parts, (text, pct) => Ui(() => p.Busy = $"{text}  ({pct} %)")));
            Dialogs.Toast($"AI editing {done} (v{m.ModuleVersion})");
            return true;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or InvalidDataException or UnauthorizedAccessException)
        {
            Dialogs.Error("AI editing", $"The download did not finish ({e.Message}).\n\nWhat was downloaded is kept: click again to continue.");
            return false;
        }
        finally { p.Busy = ""; }
    }

    /// <summary>Registers the helper as the "clipshrinker" MCP server in Claude Code (all projects of this user).</summary>
    [RelayCommand]
    Task ConnectAiClaude(ModuleItem p) => ConnectAi(p, ask: false);

    async Task ConnectAi(ModuleItem p, bool ask)
    {
        if (!AiModule.IsInstalled(AppPaths.ExeDir)) return;
        string? claude = AiModule.FindClaude();
        string cmd = AiModule.AddCommand(AppPaths.ExeDir);
        if (claude is null)
        {
            if (Dialogs.Choice("Connect to Claude Code",
                    "Claude Code (claude.exe) was not found on this PC.\n\nInstall Claude Code, then click \"Connect to Claude Code\" here " +
                    "again, or run this command in a terminal:\n\n" + cmd,
                    ["Copy the command", "Close"], 0, 1) == 0)
                TryCopy(cmd);
            return;
        }
        if (ask && !Dialogs.YesNo("Connect to Claude Code",
                "Register the AI editing helper in Claude Code now (as the \"clipshrinker\" MCP server, for all your projects)?\n\n" +
                "It replaces an older \"clipshrinker\" entry. Start a new Claude Code session afterwards.")) return;
        p.Busy = "Registering in Claude Code...";
        try
        {
            await Task.Run(() => AiModule.Register(claude, AppPaths.ExeDir));
            Dialogs.Info("Connect to Claude Code",
                "Done: Claude Code starts the AI editing helper as \"clipshrinker\".\n\nStart a NEW Claude Code session, keep Clip Shrinker " +
                "open and ask Claude to edit your clips. (If you move the Clip Shrinker folder, connect again.)");
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (Dialogs.Choice("Connect to Claude Code", $"Claude Code did not accept it ({e.Message}).\n\nYou can run this command in a terminal:\n\n{cmd}",
                    ["Copy the command", "Close"], 0, 1) == 0)
                TryCopy(cmd);
        }
        finally { p.Busy = ""; }
    }

    void TryCopy(string text)
    {
        try { Clipboard.SetText(text); Dialogs.Toast("Command copied"); }
        catch (System.Runtime.InteropServices.ExternalException) { Dialogs.Toast("The clipboard is busy: try again"); }
    }

    /// <summary>The bin button on the AI editing card: the downloaded parts go (module.json stays, so it can be
    /// installed again), and Claude Code's "clipshrinker" entry when it points here.</summary>
    async Task UninstallAi(ModuleItem p)
    {
        long size = 0;
        try { size = new DirectoryInfo(AiModule.Dir(AppPaths.ExeDir)).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (!Dialogs.YesNo("Uninstall AI editing",
                $"Delete the AI editing helper, its ffmpeg and the speech model ({Mb(size)}) and remove it from Claude Code?\n\n" +
                $"{AiModule.Dir(AppPaths.ExeDir)}\n\nIt can be installed again later.")) return;
        p.Busy = "Uninstalling...";
        bool unreg = false, clean;
        try
        {
            if (AiModule.FindClaude() is { } claude)
                try { unreg = await Task.Run(() => AiModule.Unregister(claude, AppPaths.ExeDir)); }
                catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { Debug.WriteLine($"unregister: {e.Message}"); }
            clean = await Task.Run(() => AiModule.Uninstall(AppPaths.ExeDir));
        }
        finally { p.Busy = ""; }
        p.SetEnabled(false);
        moduleFlags[p.Id] = false;
        p.Installed = false;
        p.Update = "";
        ApplyModules();
        ShowAiUpdate();
        Dialogs.Toast("AI editing uninstalled" + (unreg ? " and removed from Claude Code" : "") +
                      (clean ? "" : " (a running Claude session still holds the helper: it is deleted at the next start)"));
    }
}
