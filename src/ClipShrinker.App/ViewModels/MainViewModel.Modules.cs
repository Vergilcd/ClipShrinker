using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Modules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipShrinker.App.ViewModels;

/// <summary>One row of the Modules window. A deleted built-in module stays in the list, greyed out, with Restore.</summary>
public sealed partial class ModuleItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public bool Builtin { get; init; }
    /// <summary>2.8: the module's parts are downloaded from GitHub (AI editing): Install / Update / Uninstall.</summary>
    public bool Downloadable { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Byline))]
    private string name = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Byline))]
    private string author = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Byline))]
    private string version = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string changelog = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(Byline), nameof(CanInstall), nameof(CanUpdate), nameof(CanConnect))]
    private bool installed;

    /// <summary>2.8 (Downloadable): "" or what an update brings ("v0.4.0, 21 MB").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate), nameof(Byline))]
    private string update = "";

    /// <summary>2.8 (Downloadable): download size of a first install, for the Byline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Byline))]
    private string installSize = "";

    [ObservableProperty] private bool enabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(CanInstall), nameof(CanUpdate), nameof(CanConnect))]
    private string busy = "";

    /// <summary>Deleted, but it came with the app: it can be downloaded again.</summary>
    public bool CanRestore => !Installed && Builtin && !Downloadable && Busy.Length == 0;
    public bool CanInstall => !Installed && Downloadable && Busy.Length == 0;
    public bool CanUpdate => Installed && Downloadable && Update.Length > 0 && Busy.Length == 0;
    public bool CanConnect => Installed && Downloadable && Busy.Length == 0;

    public string Byline => Installed ? $"v{Version}  ·  by {Author}" + (Downloadable ? "  ·  downloaded from GitHub" : Builtin ? "  ·  included with Clip Shrinker" : "")
                                        + (Update.Length > 0 ? $"  ·  update available ({Update})" : "")
                          : Downloadable ? "Not installed  ·  click Install to download it from GitHub" + (InstallSize.Length > 0 ? $" ({InstallSize})" : "")
                          : "Deleted  ·  click Restore to download it again from GitHub";

    /// <summary>Raised when the switch is flipped (not when the app sets it).</summary>
    public event Action<ModuleItem>? Toggled;
    bool quiet;

    public void SetEnabled(bool on)
    {
        quiet = true;
        Enabled = on;
        quiet = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!quiet) Toggled?.Invoke(this);
    }

    public void Fill(ModuleManifest m, string? changelog)
    {
        Name = m.Name;
        Author = m.Author;
        Version = m.Version;
        Description = m.Description;
        Changelog = changelog is null ? "This module has no CHANGELOG.txt." : Reflow(changelog);
    }

    /// <summary>The text files wrap their bullet points at ~115 characters; the narrow box wraps by itself, so an
    /// indented continuation line is joined to the line above it.</summary>
    static string Reflow(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("  ") && raw.Trim().Length > 0 && lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines[^1] += " " + raw.Trim();
            else lines.Add(raw.TrimEnd());
        }
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>The modules: modules\&lt;id&gt;\ folders with module.json and CHANGELOG.txt, switched on and off in the
/// Modules window. The three that come with the app (Timeline effects, Bitrate presets, Picture adjustments) run
/// code that is part of the program; their folder holds the version, the changelog and their files (RIFE).</summary>
public sealed partial class MainViewModel
{
    /// <summary>Where Restore downloads a built-in module's files from (the repository's modules\ folder).</summary>
    const string ModuleRawBase = "https://raw.githubusercontent.com/Vergilcd/ClipShrinker/main/modules";

    public ObservableCollection<ModuleItem> Modules { get; } = [];
    readonly Dictionary<string, bool> moduleFlags = [];
    readonly HashSet<string> modulesKnown = [];

    /// <summary>Raised after a module was switched on / off, deleted or restored (the windows update their buttons).</summary>
    public event Action? ModulesChanged;

    public bool EffectsModuleOn => ModuleOn(ModuleIds.TimelineEffects);
    public bool PresetsModuleOn => ModuleOn(ModuleIds.BitratePresets);
    public bool PictureModuleOn => ModuleOn(ModuleIds.PictureAdjust);
    public bool EqModuleOn => ModuleOn(ModuleIds.Equalizer);
    public string ModulesDirText => AppPaths.ModulesDir;

    bool ModuleOn(string id) => Modules.FirstOrDefault(p => p.Id == id) is { Installed: true, Enabled: true };

    /// <summary>Start-up: built-in modules are written to modules\ the first time (a deleted one is not brought back),
    /// RIFE moves into its module, then the list is read and the switches applied.</summary>
    void InitModules()
    {
        bool renamed = AppPaths.MoveOldModulesFolder();            // 2.7: "plugins" are called "modules" now
        if ((settings["modules"] ?? settings["plugins"]) is JsonObject flags)
            foreach (var (id, v) in flags)
                if (Core.Py.PyJson.Bool(v) is { } on) moduleFlags[id] = on;
        if ((settings["modules_known"] ?? settings["plugins_known"]) is JsonArray known)
            foreach (var k in known)
                if (Core.Py.PyJson.Str(k) is { } s) modulesKnown.Add(s);
        foreach (var id in ModuleIds.Builtin)
        {
            bool there = Directory.Exists(AppPaths.ModuleDir(id));
            // 2.5: a built-in module from an older app version gets this version's module.json + changelog
            // 2.7: right after the plugins\ folder became modules\ the built-in texts are rewritten (they said "plugin")
            bool older = there && renamed || there && ModuleManifest.Read(AppPaths.ModuleDir(id)) is { } cur && EmbeddedManifest(id) is { } emb
                         && ModuleManifest.CompareVersions(cur.Version, emb.Version) < 0;
            if ((!there && !modulesKnown.Contains(id)) || older)
            {
                try { WriteBuiltinFiles(id); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.WriteLine($"module {id}: {e.Message}"); }
            }
            modulesKnown.Add(id);
        }
        AppPaths.MoveOldRife();
        LoadModuleList();
        ApplyModules(startup: true);
        InitAiModule();                                          // 2.8: AI editing is downloaded on demand
    }

    void LoadModuleList()
    {
        foreach (var p in Modules) p.Toggled -= OnModuleToggled;
        Modules.Clear();
        var found = ModuleManifest.Scan(AppPaths.ModulesDir).ToDictionary(x => x.Manifest.Id, x => x);
        foreach (var id in ModuleIds.Builtin)
        {
            string dir = AppPaths.ModuleDir(id);
            bool ai = id == ModuleIds.AiAgent;
            var item = new ModuleItem { Id = id, Dir = dir, Builtin = true, Downloadable = ai };
            if (found.Remove(id, out var f))
            {
                item.Fill(f.Manifest, ModuleManifest.ReadChangelog(f.Dir));
                // 2.8: AI editing is listed by its module.json but works only once its parts are downloaded
                item.Installed = !ai || AiModule.IsInstalled(AppPaths.ExeDir);
                if (ai && item.Installed) item.Version = AiModule.InstalledVersion(AppPaths.ExeDir);
            }
            else if (EmbeddedManifest(id) is { } m) item.Fill(m, null);
            Add(item);
        }
        foreach (var (id, (dir, m)) in found)                  // other modules someone put in the folder
        {
            var item = new ModuleItem { Id = id, Dir = dir, Builtin = false, Installed = true };
            item.Fill(m, ModuleManifest.ReadChangelog(dir));
            item.Description += (item.Description.Length > 0 ? "\n" : "") + "(Only the modules that come with Clip Shrinker can run in this version.)";
            Add(item);
        }

        void Add(ModuleItem item)
        {
            item.SetEnabled(item.Installed && (moduleFlags.TryGetValue(item.Id, out var on) ? on : ModuleIds.DefaultOn(item.Id)));
            item.Toggled += OnModuleToggled;
            Modules.Add(item);
        }
    }

    void OnModuleToggled(ModuleItem p)
    {
        if (!p.Installed) { p.SetEnabled(false); return; }
        // 2.8: AI editing sends frames, speech and file names to a third party - only on after the notice is accepted
        if (p.Id == ModuleIds.AiAgent && p.Enabled &&
            Dialogs.Choice(ModuleIds.AiAgentNoticeTitle, ModuleIds.AiAgentNotice, ["I understand, switch on", "Cancel"], 1, 1) != 0)
        {
            p.SetEnabled(false);
            return;
        }
        moduleFlags[p.Id] = p.Enabled;
        ApplyModules();
        Dialogs.Toast($"{p.Name} " + (p.Enabled ? "switched on" : "switched off"));
    }

    /// <summary>Hands the switches to the engine and refreshes everything that depends on them.</summary>
    void ApplyModules(bool startup = false)
    {
        Features.TimelineEffects = EffectsModuleOn;
        Features.BitratePresets = PresetsModuleOn;
        S.PictureOn = PictureModuleOn;
        S.EqOn = EqModuleOn;
        Features.SoundTrack = SoundModuleOn;
        OnPropertyChanged(nameof(EffectsModuleOn));
        OnPropertyChanged(nameof(PresetsModuleOn));
        OnPropertyChanged(nameof(PictureModuleOn));
        OnPropertyChanged(nameof(ShowPicturePanel));
        OnPropertyChanged(nameof(EqModuleOn));
        OnPropertyChanged(nameof(SoundModuleOn));
        OnPropertyChanged(nameof(ShowEqPanel));
        if (SoundModuleOn) EnsureSoundFolder();
        ApplyAgent();                                            // (dev: AI editing module)
        if (startup) return;
        StopPlayback();
        foreach (var c in Clips) c.NotifyChanged();            // the timelines show / hide their effect sections
        Refresh();
        RefreshRife();
        PreviewSettingsChanged();
        BracketChanged?.Invoke();
        ModulesChanged?.Invoke();
    }

    // ------------------------------------------------------------------ Picture adjustments (preview overlay)
    /// <summary>A slider or typed value changed: the preview shows it (playback restarts with the new filter).</summary>
    void SetPic(ref int field, int value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        value = name == nameof(PicDenoise) ? PictureAdjust.ClampDenoise(value) : PictureAdjust.Clamp(value);
        if (field == value) { OnPropertyChanged(name + "Text"); return; }   // (a typed "250" shows "+100%" again)
        field = value;
        OnPropertyChanged(name);
        OnPropertyChanged(name + "Text");
        OnPropertyChanged(nameof(PictureChanged));
        PreviewSettingsChanged();
    }

    public int PicBright { get => S.PicBright; set => SetPic(ref S.PicBright, value); }
    public int PicContrast { get => S.PicContrast; set => SetPic(ref S.PicContrast, value); }
    public int PicSat { get => S.PicSat; set => SetPic(ref S.PicSat, value); }
    public int PicDenoise { get => S.PicDenoise; set => SetPic(ref S.PicDenoise, value); }
    public int PicTemp { get => S.PicTemp; set => SetPic(ref S.PicTemp, value); }
    public int PicTint { get => S.PicTint; set => SetPic(ref S.PicTint, value); }

    /// <summary>The value boxes next to the sliders: "+25%"; "25", "25%" or "-40 %" can be typed (Enter confirms).
    /// Text that is not a number puts the old value back.</summary>
    public string PicBrightText { get => PictureAdjust.FormatPercent(PicBright); set => PicBright = PictureAdjust.ParsePercent(value) ?? PicBright; }
    public string PicContrastText { get => PictureAdjust.FormatPercent(PicContrast); set => PicContrast = PictureAdjust.ParsePercent(value) ?? PicContrast; }
    public string PicSatText { get => PictureAdjust.FormatPercent(PicSat); set => PicSat = PictureAdjust.ParsePercent(value) ?? PicSat; }
    public string PicDenoiseText { get => PictureAdjust.FormatDenoise(PicDenoise); set => PicDenoise = PictureAdjust.ParseDenoise(value) ?? PicDenoise; }
    public string PicTempText { get => PictureAdjust.FormatPercent(PicTemp); set => PicTemp = PictureAdjust.ParsePercent(value) ?? PicTemp; }
    public string PicTintText { get => PictureAdjust.FormatPercent(PicTint); set => PicTint = PictureAdjust.ParsePercent(value) ?? PicTint; }

    /// <summary>The Picture card over the preview is open (shared by the main window and full screen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPicturePanel))]
    private bool picturePanelOpen;

    public bool ShowPicturePanel => PicturePanelOpen && PictureModuleOn;

    [RelayCommand]
    void TogglePicturePanel() { PicturePanelOpen = !PicturePanelOpen; if (PicturePanelOpen) EqPanelOpen = false; }

    /// <summary>2.6: the denoise filter is in this ffmpeg (an older slim build in library does not have it).</summary>
    public bool DenoiseAvailable => S.DenoiseOk;

    /// <summary>Something is adjusted: the overlay button's icon is yellow.</summary>
    public bool PictureChanged => S.PictureChanged;

    [RelayCommand]
    void ResetPicture() => PicBright = PicContrast = PicSat = PicDenoise = PicTemp = PicTint = 0;

    // ------------------------------------------------------------------ Sound equalizer (preview overlay, 2.6)
    /// <summary>A band or the volume changed: the preview sound follows (playback restarts with the new filter).</summary>
    void SetEq(int index, int value)
    {
        int v = index < 0 ? Equalizer.ClampVolume(value) : Equalizer.ClampBand(value);
        string name = index < 0 ? nameof(EqVolume) : $"EqBand{index}";
        ref int field = ref (index < 0 ? ref S.EqVolume : ref S.EqBands[index]);
        if (field == v) { OnPropertyChanged(name + "Text"); return; }   // (a typed "50" shows "+12 dB" again)
        field = v;
        OnPropertyChanged(name);
        OnPropertyChanged(name + "Text");
        OnPropertyChanged(nameof(EqChanged));
        PreviewSettingsChanged();
    }

    string EqText(int v) => Equalizer.FormatDb(v);
    void SetEqText(int index, string value)
    {
        int cur = index < 0 ? S.EqVolume : S.EqBands[index];
        SetEq(index, Equalizer.ParseDb(value) ?? cur);
    }

    public int EqBand0 { get => S.EqBands[0]; set => SetEq(0, value); }
    public int EqBand1 { get => S.EqBands[1]; set => SetEq(1, value); }
    public int EqBand2 { get => S.EqBands[2]; set => SetEq(2, value); }
    public int EqBand3 { get => S.EqBands[3]; set => SetEq(3, value); }
    public int EqBand4 { get => S.EqBands[4]; set => SetEq(4, value); }
    public int EqVolume { get => S.EqVolume; set => SetEq(-1, value); }
    /// <summary>The dB boxes next to the sliders: "+3 dB"; "3", "-6dB" or "2 db" can be typed (Enter confirms).</summary>
    public string EqBand0Text { get => EqText(S.EqBands[0]); set => SetEqText(0, value); }
    public string EqBand1Text { get => EqText(S.EqBands[1]); set => SetEqText(1, value); }
    public string EqBand2Text { get => EqText(S.EqBands[2]); set => SetEqText(2, value); }
    public string EqBand3Text { get => EqText(S.EqBands[3]); set => SetEqText(3, value); }
    public string EqBand4Text { get => EqText(S.EqBands[4]); set => SetEqText(4, value); }
    public string EqVolumeText { get => EqText(S.EqVolume); set => SetEqText(-1, value); }

    /// <summary>The Sound card over the preview is open (shared by the main window and full screen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEqPanel))]
    private bool eqPanelOpen;

    public bool ShowEqPanel => EqPanelOpen && EqModuleOn;

    [RelayCommand]
    void ToggleEqPanel() { EqPanelOpen = !EqPanelOpen; if (EqPanelOpen) PicturePanelOpen = false; }

    /// <summary>Something is changed: the overlay button's icon is yellow.</summary>
    public bool EqChanged => S.EqChanged;

    [RelayCommand]
    void ResetEq()
    {
        for (int i = 0; i < S.EqBands.Length; i++) SetEq(i, 0);
        SetEq(-1, 0);
    }

    // ------------------------------------------------------------------ Modules window commands
    [RelayCommand]
    void OpenModulesFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ModulesDir);
            Process.Start(new ProcessStartInfo(AppPaths.ModulesDir) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Dialogs.Toast($"Could not open the folder: {e.Message}");
        }
    }

    [RelayCommand]
    void DeleteModule(ModuleItem p)
    {
        if (!p.Installed) return;
        if (Exporting) { Dialogs.Toast("Wait for the export to finish before deleting a module."); return; }
        if (p.Downloadable) { _ = UninstallAi(p); return; }
        string again = p.Builtin ? "It can be downloaded again later with Restore." : "This cannot be undone.";
        if (!Dialogs.YesNo("Delete module", $"Delete \"{p.Name}\" and everything in its folder?\n\n{p.Dir}\n\n{again}")) return;
        StopPlayback();
        try { if (Directory.Exists(p.Dir)) Directory.Delete(p.Dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error("Delete module", $"The folder could not be deleted ({e.Message}).");
            LoadModuleList();                                  // (part of it may be gone)
            ApplyModules();
            return;
        }
        if (p.Builtin)
        {
            p.Installed = false;
            p.SetEnabled(false);
        }
        else
        {
            p.Toggled -= OnModuleToggled;
            Modules.Remove(p);
        }
        ApplyModules();
        Dialogs.Toast($"{p.Name} deleted");
    }

    /// <summary>Downloads a deleted built-in module's files from GitHub (the copy inside the app when GitHub
    /// cannot be reached) and switches it on again.</summary>
    [RelayCommand]
    async Task RestoreModule(ModuleItem p)
    {
        if (p.Installed || !p.Builtin || p.Busy.Length > 0) return;
        p.Busy = "Downloading from GitHub...";
        bool online = true;
        var files = new Dictionary<string, byte[]>();
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{C.AppName}/{C.AppVersion}");
            foreach (var name in new[] { ModuleManifest.FileName, ModuleManifest.ChangelogName })
            {
                byte[]? data = null;
                try { data = await http.GetByteArrayAsync($"{ModuleRawBase}/{p.Id}/{name}"); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { Debug.WriteLine($"restore {p.Id}/{name}: {e.Message}"); }
                if (data is not null && name == ModuleManifest.FileName
                    && ModuleManifest.Parse(System.Text.Encoding.UTF8.GetString(data), p.Id)?.Id != p.Id) data = null;   // not this module's file
                if (data is null) { online = false; data = Embedded(p.Id, name); }
                if (data is not null) files[name] = data;
            }
        }
        try
        {
            Directory.CreateDirectory(p.Dir);
            foreach (var (name, data) in files) await File.WriteAllBytesAsync(Path.Combine(p.Dir, name), data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            p.Busy = "";
            Dialogs.Error("Restore module", $"The module folder could not be written ({e.Message}).");
            return;
        }
        p.Busy = "";
        if (ModuleManifest.Read(p.Dir) is { } m) p.Fill(m, ModuleManifest.ReadChangelog(p.Dir));
        p.Installed = true;
        p.SetEnabled(true);
        moduleFlags[p.Id] = true;
        ApplyModules();
        string where = online ? "from GitHub" : "(GitHub could not be reached - used the copy inside the app)";
        string rife = p.Id == ModuleIds.TimelineEffects && !RifeInstalled ? " RIFE can be downloaded again under Effects > Slow motion." : "";
        Dialogs.Toast($"{p.Name} restored {where}.{rife}");
    }

    // ------------------------------------------------------------------ the built-in modules' files inside the exe
    static byte[]? Embedded(string id, string file)
    {
        var asm = typeof(MainViewModel).Assembly;
        string want = $"modules/{id}/{file}";
        string? res = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == want);
        if (res is null) return null;
        using var s = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static ModuleManifest? EmbeddedManifest(string id) =>
        Embedded(id, ModuleManifest.FileName) is { } b ? ModuleManifest.Parse(System.Text.Encoding.UTF8.GetString(b), id) : null;

    static void WriteBuiltinFiles(string id)
    {
        string dir = AppPaths.ModuleDir(id);
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { ModuleManifest.FileName, ModuleManifest.ChangelogName })
            if (Embedded(id, name) is { } data) File.WriteAllBytes(Path.Combine(dir, name), data);
    }

    /// <summary>settings.json: which modules are on, and which ones the app has seen (a deleted one is not re-created).</summary>
    void SaveModuleSettings()
    {
        foreach (var p in Modules) if (p.Installed) moduleFlags[p.Id] = p.Enabled;
        settings["modules"] = new JsonObject(moduleFlags.OrderBy(kv => kv.Key).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value))));
        settings["modules_known"] = new JsonArray(modulesKnown.OrderBy(k => k).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray());
        settings.Remove("plugins");                                // (the names before 2.7)
        settings.Remove("plugins_known");
    }
}
