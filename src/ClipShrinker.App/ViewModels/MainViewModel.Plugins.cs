using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipShrinker.App.ViewModels;

/// <summary>One row of the Plugins window. A deleted built-in plugin stays in the list, greyed out, with Restore.</summary>
public sealed partial class PluginItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public bool Builtin { get; init; }

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
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(Byline))]
    private bool installed;

    [ObservableProperty] private bool enabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore))]
    private string busy = "";

    /// <summary>Deleted, but it came with the app: it can be downloaded again.</summary>
    public bool CanRestore => !Installed && Builtin && Busy.Length == 0;

    public string Byline => Installed ? $"v{Version}  ·  by {Author}" + (Builtin ? "  ·  included with Clip Shrinker" : "")
                                      : "Deleted  ·  click Restore to download it again from GitHub";

    /// <summary>Raised when the switch is flipped (not when the app sets it).</summary>
    public event Action<PluginItem>? Toggled;
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

    public void Fill(PluginManifest m, string? changelog)
    {
        Name = m.Name;
        Author = m.Author;
        Version = m.Version;
        Description = m.Description;
        Changelog = changelog is null ? "This plugin has no CHANGELOG.txt." : Reflow(changelog);
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

/// <summary>The plugins: plugins\&lt;id&gt;\ folders with plugin.json and CHANGELOG.txt, switched on and off in the
/// Plugins window. The three that come with the app (Timeline effects, Bitrate presets, Picture adjustments) run
/// code that is part of the program; their folder holds the version, the changelog and their files (RIFE).</summary>
public sealed partial class MainViewModel
{
    /// <summary>Where Restore downloads a built-in plugin's files from (the repository's plugins\ folder).</summary>
    const string PluginRawBase = "https://raw.githubusercontent.com/Vergilcd/ClipShrinker/main/plugins";

    public ObservableCollection<PluginItem> Plugins { get; } = [];
    readonly Dictionary<string, bool> pluginFlags = [];
    readonly HashSet<string> pluginsKnown = [];

    /// <summary>Raised after a plugin was switched on / off, deleted or restored (the windows update their buttons).</summary>
    public event Action? PluginsChanged;

    public bool EffectsPluginOn => PluginOn(PluginIds.TimelineEffects);
    public bool PresetsPluginOn => PluginOn(PluginIds.BitratePresets);
    public bool PicturePluginOn => PluginOn(PluginIds.PictureAdjust);
    public string PluginsDirText => AppPaths.PluginsDir;

    bool PluginOn(string id) => Plugins.FirstOrDefault(p => p.Id == id) is { Installed: true, Enabled: true };

    /// <summary>Start-up: built-in plugins are written to plugins\ the first time (a deleted one is not brought back),
    /// RIFE moves into its plugin, then the list is read and the switches applied.</summary>
    void InitPlugins()
    {
        if (settings["plugins"] is JsonObject flags)
            foreach (var (id, v) in flags)
                if (Core.Py.PyJson.Bool(v) is { } on) pluginFlags[id] = on;
        if (settings["plugins_known"] is JsonArray known)
            foreach (var k in known)
                if (Core.Py.PyJson.Str(k) is { } s) pluginsKnown.Add(s);
        foreach (var id in PluginIds.Builtin)
        {
            bool there = Directory.Exists(AppPaths.PluginDir(id));
            // 2.5: a built-in plugin from an older app version gets this version's plugin.json + changelog
            bool older = there && PluginManifest.Read(AppPaths.PluginDir(id)) is { } cur && EmbeddedManifest(id) is { } emb
                         && PluginManifest.CompareVersions(cur.Version, emb.Version) < 0;
            if ((!there && !pluginsKnown.Contains(id)) || older)
            {
                try { WriteBuiltinFiles(id); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.WriteLine($"plugin {id}: {e.Message}"); }
            }
            pluginsKnown.Add(id);
        }
        AppPaths.MoveOldRife();
        LoadPluginList();
        ApplyPlugins(startup: true);
    }

    void LoadPluginList()
    {
        foreach (var p in Plugins) p.Toggled -= OnPluginToggled;
        Plugins.Clear();
        var found = PluginManifest.Scan(AppPaths.PluginsDir).ToDictionary(x => x.Manifest.Id, x => x);
        foreach (var id in PluginIds.Builtin)
        {
            string dir = AppPaths.PluginDir(id);
            var item = new PluginItem { Id = id, Dir = dir, Builtin = true };
            if (found.Remove(id, out var f)) { item.Fill(f.Manifest, PluginManifest.ReadChangelog(f.Dir)); item.Installed = true; }
            else if (EmbeddedManifest(id) is { } m) item.Fill(m, null);
            Add(item);
        }
        foreach (var (id, (dir, m)) in found)                  // other plugins someone put in the folder
        {
            var item = new PluginItem { Id = id, Dir = dir, Builtin = false, Installed = true };
            item.Fill(m, PluginManifest.ReadChangelog(dir));
            item.Description += (item.Description.Length > 0 ? "\n" : "") + "(Only the plugins that come with Clip Shrinker can run in this version.)";
            Add(item);
        }

        void Add(PluginItem item)
        {
            item.SetEnabled(item.Installed && (pluginFlags.TryGetValue(item.Id, out var on) ? on : PluginIds.DefaultOn(item.Id)));
            item.Toggled += OnPluginToggled;
            Plugins.Add(item);
        }
    }

    void OnPluginToggled(PluginItem p)
    {
        if (!p.Installed) { p.SetEnabled(false); return; }
        pluginFlags[p.Id] = p.Enabled;
        ApplyPlugins();
        Dialogs.Toast($"{p.Name} " + (p.Enabled ? "switched on" : "switched off"));
    }

    /// <summary>Hands the switches to the engine and refreshes everything that depends on them.</summary>
    void ApplyPlugins(bool startup = false)
    {
        Features.TimelineEffects = EffectsPluginOn;
        Features.BitratePresets = PresetsPluginOn;
        S.PictureOn = PicturePluginOn;
        OnPropertyChanged(nameof(EffectsPluginOn));
        OnPropertyChanged(nameof(PresetsPluginOn));
        OnPropertyChanged(nameof(PicturePluginOn));
        OnPropertyChanged(nameof(ShowPicturePanel));
        if (startup) return;
        StopPlayback();
        foreach (var c in Clips) c.NotifyChanged();            // the timelines show / hide their effect sections
        Refresh();
        RefreshRife();
        PreviewSettingsChanged();
        BracketChanged?.Invoke();
        PluginsChanged?.Invoke();
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

    /// <summary>The value boxes next to the sliders: "+25%"; "25", "25%" or "-40 %" can be typed (Enter confirms).
    /// Text that is not a number puts the old value back.</summary>
    public string PicBrightText { get => PictureAdjust.FormatPercent(PicBright); set => PicBright = PictureAdjust.ParsePercent(value) ?? PicBright; }
    public string PicContrastText { get => PictureAdjust.FormatPercent(PicContrast); set => PicContrast = PictureAdjust.ParsePercent(value) ?? PicContrast; }
    public string PicSatText { get => PictureAdjust.FormatPercent(PicSat); set => PicSat = PictureAdjust.ParsePercent(value) ?? PicSat; }
    public string PicDenoiseText { get => PictureAdjust.FormatDenoise(PicDenoise); set => PicDenoise = PictureAdjust.ParseDenoise(value) ?? PicDenoise; }

    /// <summary>The Picture card over the preview is open (shared by the main window and full screen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPicturePanel))]
    private bool picturePanelOpen;

    public bool ShowPicturePanel => PicturePanelOpen && PicturePluginOn;

    [RelayCommand]
    void TogglePicturePanel() => PicturePanelOpen = !PicturePanelOpen;

    /// <summary>Something is adjusted: the overlay button's icon is yellow.</summary>
    public bool PictureChanged => S.PictureChanged;

    [RelayCommand]
    void ResetPicture() => PicBright = PicContrast = PicSat = PicDenoise = 0;

    // ------------------------------------------------------------------ Plugins window commands
    [RelayCommand]
    void OpenPluginsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.PluginsDir);
            Process.Start(new ProcessStartInfo(AppPaths.PluginsDir) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Dialogs.Toast($"Could not open the folder: {e.Message}");
        }
    }

    [RelayCommand]
    void DeletePlugin(PluginItem p)
    {
        if (!p.Installed) return;
        if (Exporting) { Dialogs.Toast("Wait for the export to finish before deleting a plugin."); return; }
        string again = p.Builtin ? "It can be downloaded again later with Restore." : "This cannot be undone.";
        if (!Dialogs.YesNo("Delete plugin", $"Delete \"{p.Name}\" and everything in its folder?\n\n{p.Dir}\n\n{again}")) return;
        StopPlayback();
        try { if (Directory.Exists(p.Dir)) Directory.Delete(p.Dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error("Delete plugin", $"The folder could not be deleted ({e.Message}).");
            LoadPluginList();                                  // (part of it may be gone)
            ApplyPlugins();
            return;
        }
        if (p.Builtin)
        {
            p.Installed = false;
            p.SetEnabled(false);
        }
        else
        {
            p.Toggled -= OnPluginToggled;
            Plugins.Remove(p);
        }
        ApplyPlugins();
        Dialogs.Toast($"{p.Name} deleted");
    }

    /// <summary>Downloads a deleted built-in plugin's files from GitHub (the copy inside the app when GitHub
    /// cannot be reached) and switches it on again.</summary>
    [RelayCommand]
    async Task RestorePlugin(PluginItem p)
    {
        if (p.Installed || !p.Builtin || p.Busy.Length > 0) return;
        p.Busy = "Downloading from GitHub...";
        bool online = true;
        var files = new Dictionary<string, byte[]>();
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{C.AppName}/{C.AppVersion}");
            foreach (var name in new[] { PluginManifest.FileName, PluginManifest.ChangelogName })
            {
                byte[]? data = null;
                try { data = await http.GetByteArrayAsync($"{PluginRawBase}/{p.Id}/{name}"); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { Debug.WriteLine($"restore {p.Id}/{name}: {e.Message}"); }
                if (data is not null && name == PluginManifest.FileName
                    && PluginManifest.Parse(System.Text.Encoding.UTF8.GetString(data), p.Id)?.Id != p.Id) data = null;   // not this plugin's file
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
            Dialogs.Error("Restore plugin", $"The plugin folder could not be written ({e.Message}).");
            return;
        }
        p.Busy = "";
        if (PluginManifest.Read(p.Dir) is { } m) p.Fill(m, PluginManifest.ReadChangelog(p.Dir));
        p.Installed = true;
        p.SetEnabled(true);
        pluginFlags[p.Id] = true;
        ApplyPlugins();
        string where = online ? "from GitHub" : "(GitHub could not be reached - used the copy inside the app)";
        string rife = p.Id == PluginIds.TimelineEffects && !RifeInstalled ? " RIFE can be downloaded again under Effects > Slow motion." : "";
        Dialogs.Toast($"{p.Name} restored {where}.{rife}");
    }

    // ------------------------------------------------------------------ the built-in plugins' files inside the exe
    static byte[]? Embedded(string id, string file)
    {
        var asm = typeof(MainViewModel).Assembly;
        string want = $"plugins/{id}/{file}";
        string? res = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == want);
        if (res is null) return null;
        using var s = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static PluginManifest? EmbeddedManifest(string id) =>
        Embedded(id, PluginManifest.FileName) is { } b ? PluginManifest.Parse(System.Text.Encoding.UTF8.GetString(b), id) : null;

    static void WriteBuiltinFiles(string id)
    {
        string dir = AppPaths.PluginDir(id);
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { PluginManifest.FileName, PluginManifest.ChangelogName })
            if (Embedded(id, name) is { } data) File.WriteAllBytes(Path.Combine(dir, name), data);
    }

    /// <summary>settings.json: which plugins are on, and which ones the app has seen (a deleted one is not re-created).</summary>
    void SavePluginSettings()
    {
        foreach (var p in Plugins) if (p.Installed) pluginFlags[p.Id] = p.Enabled;
        settings["plugins"] = new JsonObject(pluginFlags.OrderBy(kv => kv.Key).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value))));
        settings["plugins_known"] = new JsonArray(pluginsKnown.OrderBy(k => k).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray());
    }
}
