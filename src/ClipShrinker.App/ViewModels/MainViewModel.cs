using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text.Json.Nodes;
using System.Windows;
using ClipShrinker.Core;
using ClipShrinker.Core.Export;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipShrinker.App.ViewModels;

public enum ProgressLook { Normal, Done, Failed }

/// <summary>The main window: export settings (backed by Core's AppState), the clip list and the export run
/// (reference App: _build_side, _update_summary, add_files, load_project, start_export, _export_worker).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    readonly JsonObject settings;
    public AppState S { get; }
    public Tools Tools { get; private set; }
    public IDialogs Dialogs { get; set; } = null!;
    List<Preset> presets;

    public ObservableCollection<ClipItemViewModel> Clips { get; } = [];
    readonly ThumbnailService thumbs = new();

    public MainViewModel()
    {
        settings = SettingsStore.Load(AppPaths.SettingsFile);
        S = AppState.FromSettings(settings);
        // (the preset hotkeys as saved: a newly added shortcut must not take one of them)
        Keys = LoadKeys(PresetStore.Load(AppPaths.PresetsFile, C.FixedKeys).Select(p => p.Key).Where(k => k.Length > 0).ToHashSet());
        presets = PresetStore.Load(AppPaths.PresetsFile, PresetReserved);
        Tools = AppPaths.LocateTools();
        if (Tools.Ffmpeg is not null) Core.Ffmpeg.PreviewCommands.Ffmpeg = Tools.Ffmpeg;
        theme = Str("theme") is "dark" or "light" ? Str("theme")! : App.SystemTheme();
        restoreSession = Bool("restore_session") ?? false;
        saveDir = Str("save_dir") ?? "";
        InitPreviewSettings();
        InitModules();
        Clips.CollectionChanged += (_, _) => { Renumber(); Refresh(); OnPropertyChanged(nameof(HasManyClips)); OnPropertyChanged(nameof(AllPresetOptions)); OnPropertyChanged(nameof(AllPreset)); };
        Refresh();
    }

    string? Str(string key) => Core.Py.PyJson.Str(settings[key]);
    bool? Bool(string key) => Core.Py.PyJson.Bool(settings[key]);
    public int? Int(string key) => Core.Py.PyJson.IsPlainInt(settings[key]) ? (int)Core.Py.PyJson.AsLong(settings[key]!) : null;
    public string? Geometry => Str("geometry");
    public bool Zoomed => Bool("zoomed") ?? false;

    /// <summary>Saved shortcuts; a damaged or clashing set falls back to the defaults (reference App.__init__).</summary>
    Dictionary<string, string> LoadKeys(IReadOnlySet<string> presetKeys)
    {
        var keys = new Dictionary<string, string>(C.KeyDefaults);
        if (settings["keys"] is JsonObject sk)
            foreach (var (a, v) in sk)
                if (keys.ContainsKey(a) && Core.Py.PyJson.Str(v) is { Length: > 0 } s && !C.FixedKeys.Contains(s))
                    keys[a] = s;
        // a shortcut that is newer than the saved set: its default key may already be taken by a rebound one
        foreach (var (a, alt) in C.KeyFallbacks)
            if (!(settings["keys"] is JsonObject saved && saved.ContainsKey(a))
                && (presetKeys.Contains(keys[a]) || keys.Any(kv => kv.Key != a && kv.Value == keys[a])))
                keys[a] = alt;
        return keys.Values.Distinct().Count() < keys.Count ? new Dictionary<string, string>(C.KeyDefaults) : keys;
    }

    /// <summary>Keys a bitrate preset cannot have: the fixed keys and whatever the shortcuts use now.</summary>
    public IReadOnlySet<string> PresetReserved => new HashSet<string>(C.FixedKeys.Concat(Keys.Values));

    public Dictionary<string, string> Keys { get; }

    /// <summary>Checks ffmpeg in the background after start-up (the first start can be slow while Windows scans the files).</summary>
    public async Task<bool> AfterStart()
    {
        if ((Tools.Ffmpeg is null || Tools.Ffprobe is null) && !await DownloadFfmpeg()) return false;
        _ = Task.Run(() =>
        {
            // 2.5: GPU denoise needs a working OpenCL device; until this answers (or without one) denoise runs on the CPU
            S.OpenCl = Tools.OpenClWorks();
            if (S.PicDenoise != 0) Ui(PreviewSettingsChanged);
            // 2.6: which decoders / encoders / filters really work on this PC
            cudaOk = Tools.CudaWorks();
            Media.PreviewService.CudaOk = cudaOk;
            S.DenoiseOk = Tools.HasFilter("nlmeans");
            var working = Tools.WorkingEncoders();
            Ui(() =>
            {
                workingEncoders = working;
                EnsureCodec();
                OnPropertyChanged(nameof(Codecs));
                OnPropertyChanged(nameof(Codec));
                OnPropertyChanged(nameof(DenoiseAvailable));
                if (working.Count == 0)
                    Dialogs.Warn("No video encoder", "None of the video encoders works with this ffmpeg on this PC " +
                                 "(NVIDIA, AMD, Intel or Windows). Update the graphics driver, or put the ffmpeg.exe and " +
                                 "ffprobe.exe of the Clip Shrinker download in the library folder.");
            });
        });
        Ui(RefreshRife);
        return true;
    }

    /// <summary>ffmpeg / ffprobe are missing (reference Setup.bat step 1): offer the one-time download into the
    /// library folder, with the progress in the loading popup. False = there is still no ffmpeg.</summary>
    async Task<bool> DownloadFfmpeg()
    {
        const string manual = "Put ffmpeg.exe and ffprobe.exe (and their DLL files) in the library folder next to this program.";
        if (!Dialogs.YesNo("ffmpeg missing", "ffmpeg.exe / ffprobe.exe were not found in the library folder.\n\n" +
                                             "Download them now (one time, about 30 - 90 MB from GitHub)?"))
        {
            Dialogs.Error("ffmpeg missing", manual);
            return false;
        }
        LoadingTitle = "Downloading ffmpeg";
        LoadingCount = "Connecting...";
        LoadingFraction = 0;
        Loading = true;
        try
        {
            await FfmpegDownloader.Install(AppPaths.LibraryDir, (text, pct) => Ui(() =>
            {
                LoadingCount = text;
                if (pct is { } p) LoadingFraction = p / 100.0;
            }));
        }
        catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            EndLoading();
            Dialogs.Error("ffmpeg download failed", $"The download failed ({e.Message}).\n\n" + manual);
            return false;
        }
        EndLoading();
        Tools = AppPaths.LocateTools();
        if (Tools.Ffmpeg is null || Tools.Ffprobe is null) { Dialogs.Error("ffmpeg missing", manual); return false; }
        Core.Ffmpeg.PreviewCommands.Ffmpeg = Tools.Ffmpeg;
        Dialogs.Toast("ffmpeg is ready");
        return true;
    }

    static void Ui(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

    // ------------------------------------------------------------------ app options
    [ObservableProperty] private string theme;
    [ObservableProperty] private bool restoreSession;
    [ObservableProperty] private string saveDir;

    public bool DarkMode
    {
        get => Theme == "dark";
        set
        {
            Theme = value ? "dark" : "light";
            App.SetTheme(Theme);
            OnPropertyChanged();
            foreach (var c in Clips) c.NotifyChanged();         // the timelines' trim stripes follow the theme
        }
    }

    // ------------------------------------------------------------------ export settings (AppState fields)
    bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name)) return false;
        Refresh();
        if (Playing && name is nameof(FastAudio) or nameof(SlowAudio) or nameof(FastPitch) or nameof(SlowPitch) or nameof(FastVol)
                or nameof(SlowVol) or nameof(ZoomFx) or nameof(ZoomSnd) or nameof(ZoomLen) or nameof(ZoomVol) or nameof(SlowInterp) or nameof(MuteMic))
            StopPlayback();                                  // the preview plays with the old effect settings
        return true;
    }

    public string Size { get => S.Size; set => Set(ref S.Size, value); }

    public string Bitrate
    {
        get => S.Bitrate;
        set
        {
            if (!SetProperty(ref S.Bitrate, value)) return;
            S.BitrateChanged();                              // the size limit / resolution list switch
            OnPropertyChanged(nameof(ResOptions));
            OnPropertyChanged(nameof(Res));
            Refresh();
        }
    }

    public string Res { get => S.Res; set { if (value is not null) Set(ref S.Res, value); } }
    public IReadOnlyList<string> ResOptions => S.BitrateMbps() is null ? C.ResSizeMode : C.ResBitrateMode;
    public bool SizeEnabled => S.BitrateMbps() is null;
    public string SizeHint => S.BitrateMbps() is null ? "per file" : "ignored";
    public string Codec { get => S.Codec; set { if (value is not null) Set(ref S.Codec, value); } }   // (null while the list changes)
    /// <summary>2.6: only the encoders that work on this PC (all of them until the start-up check has answered).</summary>
    public IReadOnlyList<string> Codecs => OfferedKeys is { Count: > 0 } w
        ? Core.Media.Encoders.All.Where(e => w.Contains(e.Key)).Select(e => e.Label).ToList() : C.Codecs;

    /// <summary>The working encoders the Codec list offers: the CPU ones only when no graphics card encoder works.</summary>
    IReadOnlyList<string>? OfferedKeys => workingEncoders is null ? null
        : workingEncoders.Any(k => Core.Media.Encoders.ByKey(k)?.Gpu == true)
            ? workingEncoders.Where(k => Core.Media.Encoders.ByKey(k)?.Gpu == true).ToList() : workingEncoders;
    /// <summary>2.6: codec label -> short hint for the open Codec list ("recommended for your NVIDIA GPU", ...).</summary>
    public IReadOnlyDictionary<string, string> CodecHints => OfferedKeys is { Count: > 0 } w
        ? Core.Media.Encoders.All.Where(e => w.Contains(e.Key)).ToDictionary(e => e.Label, e => Core.Media.Encoders.Hint(e.Key, w))
        : new Dictionary<string, string>();
    IReadOnlyList<string>? workingEncoders;
    bool cudaOk = true;

    // ------------------------------------------------------------------ GPU notice in the top bar (2.6)
    /// <summary>null = the start-up check has not answered yet; true = the chosen codec encodes on the graphics card.</summary>
    public bool? GpuOn => workingEncoders is null ? null : Core.Media.Encoders.ByLabel(S.Codec)?.Gpu == true && workingEncoders.Count > 0;

    public string GpuText => GpuOn switch { null => "Checking GPU...", true => "GPU Encoding is ON", false => "GPU Encoding is OFF" };

    /// <summary>What the start-up check found (handy for bug reports).</summary>
    public string GpuTip
    {
        get
        {
            if (workingEncoders is null) return "Testing which encoders work on this PC...";
            var ok = Core.Media.Encoders.All.Where(e => workingEncoders.Contains(e.Key)).Select(e => e.Label).ToList();
            string head = GpuOn == true
                ? $"Exports encode on the graphics card ({S.Codec})."
                : ok.Any(l => Core.Media.Encoders.ByLabel(l)?.Gpu == true)
                    ? $"{S.Codec} encodes on the CPU (slower). Pick a GPU codec in Codec to speed exports up."
                    : "No graphics card encoder works on this PC (or its driver is too old), so exports encode on the CPU (slower).";
            return head + "\n\nWorking encoders: " + (ok.Count > 0 ? string.Join(", ", ok) : "none") +
                   $"\nGPU decoding (NVIDIA CUDA): {(cudaOk ? "yes" : "no")}" +
                   $"\nGPU denoise (OpenCL): {(S.OpenCl ? "yes" : "no")}";
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Codec) or nameof(Codecs))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(CodecHints)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(GpuOn)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(GpuText)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(GpuTip)));
        }
    }

    /// <summary>A codec from settings or a project that this PC cannot run becomes the closest one that works.</summary>
    void EnsureCodec()
    {
        if (workingEncoders is null) return;
        string fit = Core.Media.Encoders.Fallback(S.Codec, OfferedKeys!);
        if (fit != S.Codec) { S.Codec = fit; OnPropertyChanged(nameof(Codec)); }
    }
    public string Crop { get => S.Crop; set { if (Set(ref S.Crop, value)) PreviewSettingsChanged(); } }
    public IReadOnlyList<string> Crops { get; } = C.Aspects.Select(a => a.Name).ToList();
    public bool Hdr
    {
        get => S.Hdr;
        set
        {
            if (!Set(ref S.Hdr, value)) return;
            foreach (var c in Clips.Where(c => c.Row.Info.Trc is not null)) c.StartThumbs();   // tone-mapped or not
            PreviewSettingsChanged();
        }
    }
    public string Audio { get => S.Audio; set => Set(ref S.Audio, value); }
    public IReadOnlyList<string> AudioRates => AppState.AudioRates;
    public string Atrk { get => S.Atrk; set => Set(ref S.Atrk, value); }
    public IReadOnlyList<string> AudioTrackModes => AppState.AudioTrackModes;
    public bool MuteMic { get => S.MuteMic; set => Set(ref S.MuteMic, value); }
    public string Prefix { get => S.Prefix; set { if (Set(ref S.Prefix, value)) NameTextChanged(); } }
    public string Suffix { get => S.Suffix; set { if (Set(ref S.Suffix, value)) NameTextChanged(); } }

    /// <summary>"Keep original file name". Shown ticked and greyed out while Prefix and Suffix are both empty
    /// (the file name would be empty without it); the saved choice comes back once one of them has text.</summary>
    public bool KeepName { get => S.KeepsName; set => Set(ref S.KeepName, value); }
    public bool KeepNameEnabled => S.HasNameText;

    void NameTextChanged()
    {
        OnPropertyChanged(nameof(KeepName));
        OnPropertyChanged(nameof(KeepNameEnabled));
    }

    // effects window
    public string Trans { get => S.Trans; set => Set(ref S.Trans, value); }
    public IReadOnlyList<string> TransitionNames { get; } = C.Transitions.Select(t => t.Name).ToList();
    /// <summary>2 or more clips: the Transition drop-down is shown (2.5).</summary>
    public bool HasManyClips => Clips.Count > 1;
    public string Tlen { get => S.Tlen; set => Set(ref S.Tlen, value); }
    public string FastAudio { get => S.FastAudio; set => Set(ref S.FastAudio, value); }
    public IReadOnlyList<string> FastAudioOptions => C.FastAudio;
    public int FastVol { get => S.FastVol; set => Set(ref S.FastVol, Snap(value)); }
    public string FastPitch { get => S.FastPitch; set => Set(ref S.FastPitch, value); }
    public IReadOnlyList<string> FastPitchOptions => C.FastPitch;
    public bool Vhs { get => S.Vhs; set => Set(ref S.Vhs, value); }
    public string SlowAudio { get => S.SlowAudio; set => Set(ref S.SlowAudio, value); }
    public IReadOnlyList<string> SlowAudioOptions => C.SlowAudio;
    public int SlowVol { get => S.SlowVol; set => Set(ref S.SlowVol, Snap(value)); }
    public string SlowPitch { get => S.SlowPitch; set => Set(ref S.SlowPitch, value); }
    public IReadOnlyList<string> SlowPitchOptions => C.SlowPitch;
    public string SlowInterp { get => S.SlowInterp; set { if (Set(ref S.SlowInterp, value)) RefreshRife(); } }
    public IReadOnlyList<string> SlowInterpOptions => C.SlowInterp;
    public string ZoomFx { get => S.ZoomFx; set => Set(ref S.ZoomFx, value); }
    public IReadOnlyList<string> ZoomFxOptions => C.ZoomFx;
    public string ZoomLen { get => S.ZoomLen; set => Set(ref S.ZoomLen, value); }
    public string ZoomSnd { get => S.ZoomSnd; set => Set(ref S.ZoomSnd, value); }
    public IReadOnlyList<string> ZoomSndOptions => C.ZoomAudio;
    public int ZoomVol { get => S.ZoomVol; set => Set(ref S.ZoomVol, Snap(value)); }

    /// <summary>The volume sliders move in steps of 5 % (reference _volume_row).</summary>
    static int Snap(int v) => Math.Max(0, Math.Min(200, (int)Math.Round(v / 5.0) * 5));

    [ObservableProperty] private string rifeStatus = "";
    public bool RifeInstalled => AppPaths.FindRife(Tools) is not null;
    [ObservableProperty] private string rifeBusy = "";       // download step text while it runs
    [ObservableProperty] private int rifePct;

    /// <summary>The download button: shown while RIFE is missing, disabled while it downloads.</summary>
    public bool ShowRifeButton => !RifeInstalled || RifeBusy.Length > 0;
    public bool RifeDownloading => RifeBusy.Length > 0;

    void RefreshRife()
    {
        RifeStatus = RifeBusy.Length > 0 ? RifeBusy
            : RifeInstalled ? "RIFE is installed (used when exporting; the preview shows blending)"
            : "RIFE is not installed" + (Text.InterpKey(S.SlowInterp) == "rife" ? " - exports use frame blending" : "");
        OnPropertyChanged(nameof(RifeInstalled));
        OnPropertyChanged(nameof(ShowRifeButton));
        OnPropertyChanged(nameof(RifeDownloading));
    }

    /// <summary>Downloads RIFE into library\rife (reference download_rife).</summary>
    [RelayCommand]
    async Task DownloadRife()
    {
        if (RifeBusy.Length > 0 || RifeInstalled) return;
        string dir = AppPaths.RifeDir;                       // (2.4: inside the Timeline effects module)
        void Step(string text, int? pct) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            RifeBusy = text;
            if (pct is { } p) RifePct = p;
            RefreshRife();
        });
        Step("Starting download...", 0);
        string msg = "";
        try { await Task.Run(() => Core.Rife.RifeDownloader.Install(dir, Step)); }
        catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            msg = $"Download failed ({e.Message}). Unzip the release from github.com/nihui/rife-ncnn-vulkan into the modules\\timeline-effects\\rife folder.";
        }
        await Application.Current.Dispatcher.InvokeAsync(() => { });   // let the last progress step land first
        RifeBusy = "";
        RefreshRife();
        if (msg.Length > 0) { RifeStatus = msg; Dialogs.Toast("RIFE download failed - see Effects"); }        else Dialogs.Toast("RIFE installed - choose it under Effects > Slow motion effects > Smoothing");
    }

    // ------------------------------------------------------------------ summary
    [ObservableProperty] private string summary1 = "";
    [ObservableProperty] private string summary2 = "";
    [ObservableProperty] private string summary3 = "";
    [ObservableProperty] private string summary4 = "";
    [ObservableProperty] private string cropInfo = " ";
    public bool HasClips => Clips.Count > 0;

    void Refresh()
    {
        var lines = S.SummaryLines(Clips.Select(c => c.Row.ToExport()).ToList());
        (Summary1, Summary2, Summary3, Summary4) = (lines[0], lines[1], lines[2], lines[3]);
        if (Clips.Count == 0 || S.Ratio is null) CropInfo = " ";
        else
        {
            var i = Clips[0].Row.Info;
            var (cw, ch) = Core.Geometry.CropSize(i.W, i.H, S.Ratio);
            CropInfo = (cw, ch) != (i.W, i.H) ? $"{i.W}x{i.H} -> {cw}x{ch}" : "(already narrower - no crop)";
        }
        OnPropertyChanged(nameof(SizeEnabled));
        OnPropertyChanged(nameof(SizeHint));
        OnPropertyChanged(nameof(HasClips));
    }

    void Renumber()
    {
        for (int i = 0; i < Clips.Count; i++) Clips[i].Index = i;
    }

    // ------------------------------------------------------------------ clips
    [RelayCommand]
    void AddVideos()
    {
        if (Exporting) return;
        var paths = Dialogs.OpenVideos();
        if (paths.Length > 0) _ = AddFiles(paths);
    }

    // ------------------------------------------------------------------ "Loading videos" popup
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string loadingTitle = "";
    [ObservableProperty] private string loadingCount = "";
    [ObservableProperty] private double loadingFraction;
    int loadDone, loadTotal;
    DateTime loadLastStep;
    System.Windows.Threading.DispatcherTimer? loadWatch;
    const double ReadShare = 0.3;                              // the ring: reading the files, then their thumbnails

    /// <summary>Phase 1 of the popup (from 2 videos on): "Reading n / total"; each Report(1) is one file.</summary>
    IProgress<int> BeginLoading(string title, int total)
    {
        loadDone = 0;
        loadTotal = total;
        loadLastStep = DateTime.UtcNow;
        LoadingTitle = title;
        LoadingFraction = 0;
        LoadingCount = total > 0 ? $"Reading 0 / {total}" : "Reading...";
        Loading = total >= 2;
        return new Progress<int>(n =>                          // (runs on the UI thread)
        {
            loadDone += n;
            loadLastStep = DateTime.UtcNow;
            LoadingCount = loadTotal > 0 ? $"Reading {Math.Min(loadDone, loadTotal)} / {loadTotal}" : $"Reading {loadDone}";
            LoadingFraction = loadTotal > 0 ? ReadShare * Math.Min(1.0, loadDone / (double)loadTotal) : 0;
        });
    }

    /// <summary>Phase 2: the popup stays until every new clip shows its first thumbnails ("Thumbnails n / total").
    /// A clip whose thumbnails never come (a broken file) cannot keep it open: after 6 s without progress it closes.</summary>
    void TrackThumbnails(IReadOnlyList<ClipItemViewModel> items)
    {
        if (!Loading || items.Count == 0) { EndLoading(); return; }
        int total = items.Count, done = 0;
        void Step()
        {
            loadLastStep = DateTime.UtcNow;
            LoadingCount = $"Thumbnails {done} / {total}";
            LoadingFraction = ReadShare + (1 - ReadShare) * done / total;
            if (done >= total) EndLoading();
        }
        foreach (var c in items)
        {
            if (c.FirstThumbsDone) { done++; continue; }
            void Ready() { c.FirstThumbsReady -= Ready; done++; Step(); }
            c.FirstThumbsReady += Ready;
        }
        loadLastStep = DateTime.UtcNow;
        Step();
        if (!Loading) return;
        loadWatch?.Stop();
        loadWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        loadWatch.Tick += (_, _) => { if (!Loading || DateTime.UtcNow - loadLastStep > TimeSpan.FromSeconds(6)) EndLoading(); };
        loadWatch.Start();
    }

    void EndLoading()
    {
        Loading = false;
        loadWatch?.Stop();
        loadWatch = null;
    }

    /// <summary>Probes and adds videos; a .clipproj among them is opened instead (reference add_files).</summary>
    public async Task AddFiles(IReadOnlyList<string> paths)
    {
        if (Exporting) { Dialogs.Toast("Wait for the current export to finish before adding clips."); return; }
        if (paths.FirstOrDefault(p => p.EndsWith(C.ProjectExt, StringComparison.OrdinalIgnoreCase)) is { } proj)
        {
            await OpenProject(proj);
            return;
        }
        bool pushed = PushUndo("add clips");
        int serial = undoSerial;
        var failed = new List<string>();
        var progress = BeginLoading("Loading videos", paths.Count);
        var rows = await Task.Run(() =>
        {
            var res = new List<TimelineClip>();
            foreach (var p in paths)
            {
                progress.Report(1);
                if (!File.Exists(p) || !ProjectFile.VideoExts.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                {
                    failed.Add($"{Path.GetFileName(p)}: not a video file");
                    continue;
                }
                try { res.Add(new TimelineClip(p, Tools.Probe(p))); }
                catch (Exception e) when (e is MediaException or IOException or System.Text.Json.JsonException
                                              or System.ComponentModel.Win32Exception)
                {
                    failed.Add($"{Path.GetFileName(p)}: {(e.Message.Length > 120 ? e.Message[..120] : e.Message)}");
                }
            }
            return res;
        });
        ClipItemViewModel? first = null;
        var added = new List<ClipItemViewModel>();
        foreach (var r in rows)
        {
            var c = NewItem(r);
            Clips.Add(c);
            added.Add(c);
            first ??= c;
        }
        TrackThumbnails(added);
        if (first is not null) RequestPreview(first, 0.0);     // the first added clip opens with its first frame
        if (pushed && rows.Count == 0 && serial == undoSerial) DropLastUndo();   // nothing was added: no undo step
        if (failed.Count > 0) Dialogs.Warn("Some files were skipped", string.Join("\n", failed));
    }

    [RelayCommand]
    async Task OpenProjectDialog()
    {
        if (Exporting) return;
        if (Dialogs.OpenProject() is { } p) await OpenProject(p);
    }

    public string? ProjectPath { get; private set; }

    /// <summary>How many clips a project lists (for the loading counter; 0 when it cannot be read).</summary>
    static int ProjectClipCount(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("clips", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Array ? c.GetArrayLength() : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return 0; }
    }

    /// <summary>Opens a .clipproj (reference open_project / load_project). quiet = the last session at start-up:
    /// no questions, no project file name, missing clips only as a status message.</summary>
    public async Task OpenProject(string path, bool quiet = false)
    {
        if (Exporting) return;
        if (!quiet && Clips.Count > 0 && !Dialogs.YesNo("Open project", "Replace the clips that are open now?")) return;
        ProjectLoad load;
        var progress = BeginLoading(quiet ? "Restoring the last session" : "Opening the project", ProjectClipCount(path));
        try
        {
            load = await Task.Run(() => ProjectFile.Load(path, p => { progress.Report(1); return Tools.Probe(p); },
                                                         n => presets.FirstOrDefault(p => p.Name == n)));
        }
        catch (ProjectException)
        {
            EndLoading();
            if (!quiet) Dialogs.Error("Could not open the project", "This is not a valid Clip Shrinker project file.");
            return;
        }
        if (Clips.Count > 0) PushUndo("open project");
        undoOff = true;
        try
        {
            RemoveAllClips();
            S.ApplyProject(load.Settings);
            OnPropertyChanged(string.Empty);                 // every setting may have changed
            OnPropertyChanged(nameof(ResOptions));           // the resolution list first, then its value, or the
            OnPropertyChanged(nameof(Res));                  // drop-down shows nothing for "Original" (bitrate mode)
            EnsureCodec();
            foreach (var r in load.Clips) Clips.Add(NewItem(r));
        }
        finally { undoOff = false; }
        TrackThumbnails(Clips.ToList());
        if (Clips.Count > 0) RequestPreview(Clips[load.Active], Clips[load.Active].Row.Start);
        ProjectPath = quiet ? null : path;
        OnPropertyChanged(nameof(WindowTitle));
        if (load.Missing.Count > 0)
        {
            if (quiet) Dialogs.Toast($"{load.Missing.Count} clip(s) of the last session were not found");
            else Dialogs.Warn("Some clips are missing", "These files were not found and were skipped:\n\n" + string.Join("\n", load.Missing.Take(15)));
        }
        else if (load.Failed.Count > 0 && !quiet) Dialogs.Warn("Some files were skipped", string.Join("\n", load.Failed));
        else Dialogs.Toast(quiet ? "Last session restored" : $"Opened {Path.GetFileName(path)}");
    }

    public string WindowTitle => $"{C.AppName} v{C.AppVersion}" + (ProjectPath is { } p ? $" - {Path.GetFileName(p)}" : "");

    /// <summary>A clip card with its timeline wired to this window (status messages, summary, thumbnails).</summary>
    ClipItemViewModel NewItem(TimelineClip r)
    {
        var c = new ClipItemViewModel(r, thumbs, () => S.Hdr);
        c.Editor.Playhead = r.Start;                             // (reference _load_clips: the playhead starts at the trim start)
        c.SetPresets(presets);
        c.Editor.Status = Dialogs.Toast;
        c.Editor.PushUndo = label => PushUndo(label);
        c.Editor.PreviewRequested = t => RequestPreview(c, t);
        c.Changed += Refresh;
        c.Changed += () => BracketChanged?.Invoke();             // the playhead may have moved into / out of a zoom
        c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ClipItemViewModel.PresetLabel)) OnPropertyChanged(nameof(AllPreset)); };
        return c;
    }

    // ------------------------------------------------------------------ one bitrate preset for every clip
    /// <summary>"Auto bitrate" plus the presets, as the clips' title bars list them.</summary>
    public IReadOnlyList<string> AllPresetOptions => Clips.FirstOrDefault()?.PresetLabels ?? ["Auto bitrate", .. presets.Select(p => $"{p.Name} ({Core.Py.Py.G(p.Mbps)} Mbps)")];

    /// <summary>The preset all clips share (empty when they differ); choosing one gives it to every clip
    /// ("Auto bitrate" turns them all back to the size / bitrate of the Export panel).</summary>
    public string? AllPreset
    {
        get
        {
            var labels = Clips.Select(c => c.PresetLabel).Distinct().ToList();
            return labels.Count == 1 ? labels[0] : labels.Count == 0 ? "Auto bitrate" : null;
        }
        set
        {
            if (value is null || Exporting || Clips.Count == 0 || Clips.All(c => c.PresetLabel == value)) { OnPropertyChanged(); return; }
            int i = AllPresetOptions.ToList().IndexOf(value);
            string? name = i <= 0 ? null : presets[i - 1].Name;
            PushUndo("bitrate preset");
            foreach (var c in Clips) c.SetPreset(name);
            OnPropertyChanged();
            Dialogs.Toast(name is null ? "All clips: auto bitrate" : $"Bitrate preset \"{name}\" applied to all clips");
        }
    }

    // ------------------------------------------------------------------ active clip and selection
    [ObservableProperty] private ClipItemViewModel? activeClip;
    readonly HashSet<ClipItemViewModel> selection = [];
    public int SelectionCount => selection.Count;

    /// <summary>Raised when a clip should be scrolled into view (the active one after it grew).</summary>
    public event Action<ClipItemViewModel>? RevealRequested;

    /// <summary>The active clip gets the full timeline; the others are thin strips (reference set_active).</summary>
    public void SetActive(ClipItemViewModel row)
    {
        if (ReferenceEquals(ActiveClip, row)) return;
        var old = ActiveClip;
        ActiveClip = row;
        if (old is not null) old.IsActive = false;
        row.IsActive = true;
        selection.Clear();
        selection.Add(row);
        RefreshSelection();
        RevealRequested?.Invoke(row);
    }

    void RefreshSelection()
    {
        foreach (var c in Clips) c.IsSelected = selection.Contains(c);
        OnPropertyChanged(nameof(SelectionCount));
    }

    /// <summary>Click on a clip: plain = select only it, Shift = range from the active clip, Ctrl = add / remove.</summary>
    public void ClickRow(ClipItemViewModel row, bool shift, bool ctrl)
    {
        if (!Clips.Contains(row)) return;
        var act = ActiveClip is { } a && Clips.Contains(a) ? a : null;
        if (shift && act is not null)
        {
            int i = Clips.IndexOf(act), j = Clips.IndexOf(row);
            selection.Clear();
            foreach (var c in Clips.Skip(Math.Min(i, j)).Take(Math.Abs(i - j) + 1)) selection.Add(c);
        }
        else if (ctrl && act is not null)
        {
            if (!ReferenceEquals(row, act) && !selection.Remove(row)) selection.Add(row);
            selection.Add(act);
        }
        else if (ReferenceEquals(ActiveClip, row)) { selection.Clear(); selection.Add(row); }
        else SetActive(row);
        RefreshSelection();
        if (selection.Count > 1) Dialogs.Toast($"{selection.Count} clips selected - press Delete to remove them");
    }

    public void SelectAll()
    {
        if (Clips.Count == 0) return;
        if (ActiveClip is null || !Clips.Contains(ActiveClip)) SetActive(Clips[0]);
        selection.Clear();
        foreach (var c in Clips) selection.Add(c);
        RefreshSelection();
        Dialogs.Toast($"{Clips.Count} clips selected - press Delete to remove them");
    }

    void RemoveAllClips()
    {
        ClearPreview();
        foreach (var c in Clips) DetachRow(c);
        Clips.Clear();
        selection.Clear();
        ActiveClip = null;
        RefreshSelection();
    }

    [RelayCommand]
    public void Remove(ClipItemViewModel c)
    {
        if (Exporting || !Clips.Contains(c)) return;
        StopPlayback();
        PushUndo("remove clip");
        if (lastPreview is { } lp && ReferenceEquals(lp.Row, c)) { lastPreview = null; PreviewImage = null; }
        int idx = Clips.IndexOf(c);
        DetachRow(c);
        Clips.Remove(c);
        selection.Remove(c);
        if (ReferenceEquals(ActiveClip, c))
        {
            ActiveClip = null;
            if (Clips.Count > 0) SetActive(Clips[Math.Min(idx, Clips.Count - 1)]);   // the next one, or the previous
        }
        RefreshSelection();
    }

    /// <summary>Removes every selected clip (the active one is always part of the selection).</summary>
    public void DeleteSelected()
    {
        if (Exporting) return;
        var victims = Clips.Where(c => selection.Contains(c) || ReferenceEquals(c, ActiveClip)).ToList();
        if (victims.Count == 0) return;
        StopPlayback();
        int first = Clips.IndexOf(victims[0]);
        PushUndo($"remove {victims.Count} clip(s)");
        if (lastPreview is { } lp && victims.Contains(lp.Row)) { lastPreview = null; PreviewImage = null; }
        foreach (var c in victims) { DetachRow(c); Clips.Remove(c); }
        ActiveClip = null;
        selection.Clear();
        if (Clips.Count > 0) { var nxt = Clips[Math.Min(first, Clips.Count - 1)]; RequestPreview(nxt, nxt.Row.Start); }
        RefreshSelection();
        Dialogs.Toast($"Removed {victims.Count} clip(s)");
    }

    /// <summary>Delete: a selected speed / zoom section goes first, otherwise the selected clips.</summary>
    public void OnDelete()
    {
        if (Exporting || Clips.Count == 0) return;
        if (HasSelectedSound && RemoveSound(SelectedSoundClip, SelectedSound)) return;   // (2.7: a selected sound first)
        if (ActiveClip is { } row && (row.Editor.RemoveSection(row.Editor.Sel) || row.Editor.RemoveZoom(row.Editor.ZSel)))
            row.NotifyChanged();
        else DeleteSelected();
    }

    /// <summary>Backspace: only a selected sound or speed / zoom section.</summary>
    public void OnBackspace()
    {
        if (Exporting || ActiveClip is not { } row) return;
        if (HasSelectedSound && RemoveSound(SelectedSoundClip, SelectedSound)) return;
        if (row.Editor.RemoveSection(row.Editor.Sel) || row.Editor.RemoveZoom(row.Editor.ZSel)) row.NotifyChanged();
    }

    /// <summary>Live reordering while a clip's title line is dragged: the clip goes to index.</summary>
    public void MoveClipTo(ClipItemViewModel row, int index)
    {
        if (Exporting) return;
        int i = Clips.IndexOf(row);
        index = Math.Max(0, Math.Min(Clips.Count - 1, index));
        if (i < 0 || i == index) return;
        if (!reorderPushed) reorderPushed = PushUndo("reorder clips");   // one undo step per drag
        Clips.Move(i, index);
    }

    ClipItemViewModel? TargetClip => ActiveClip is { } a && Clips.Contains(a) ? a : Clips.FirstOrDefault();

    /// <summary>A rebindable hotkey (trim start / end, speed, slow, zoom). True = it was used.</summary>
    public bool OnActionKey(string act)
    {
        if (Exporting || TargetClip is not { } row) return false;
        if (act == "sound") return SoundModuleOn && AddCurrentSound();   // (2.7 Sound track module)
        if (act is "speed" or "slow" or "zoom" && !EffectsModuleOn) return false;   // (Timeline effects module)
        var ed = row.Editor;
        if (act is "speed" or "slow" or "zoom") StopPlayback();
        bool changed = act switch
        {
            "trim_start" => ed.SetTrim(true),
            "trim_end" => ed.SetTrim(false),
            "speed" => ed.AddSection(2.0) is not null,
            "slow" => ed.AddSection(0.5) is not null,
            "zoom" => ed.AddZoom() is not null,
            _ => false,
        };
        if (changed && act is "speed" or "slow" or "zoom") SetActive(row);
        row.NotifyChanged();
        return true;
    }

    /// <summary>An effect button was dropped on a clip's timeline at time t: that clip becomes the active one, the
    /// playhead goes there and the effect is added as with its hotkey.</summary>
    public void AddEffectAt(ClipItemViewModel row, string act, double t)
    {
        if (Exporting || !Clips.Contains(row)) return;
        StopPlayback();
        SetActive(row);
        row.Editor.Playhead = Math.Max(0, Math.Min(row.Editor.Dur, t));
        OnActionKey(act);
        RequestPreview(row, row.Editor.Playhead);
    }

    /// <summary>A bitrate preset's own key: applies it to the selected clips.</summary>
    public bool OnPresetKey(string key)
    {
        if (Exporting || Clips.Count == 0 || !PresetsModuleOn) return false;
        var p = presets.FirstOrDefault(q => q.Key.Length > 0 && q.Key == key);
        if (p is null) return false;
        var sel = Clips.Where(selection.Contains).ToList();
        if (sel.Count == 0 && TargetClip is { } t) sel.Add(t);
        if (sel.Any(r => r.Row.Preset != p.Name)) PushUndo("bitrate preset");
        foreach (var r in sel) r.SetPreset(p.Name);
        Dialogs.Toast($"Bitrate preset \"{p.Name}\" ({Core.Py.Py.G(p.Mbps)} Mbps) applied to " +
                      (sel.Count > 1 ? $"{sel.Count} clips" : Path.GetFileName(sel[0].Path)));
        return true;
    }

    [RelayCommand]
    void ClearAll()
    {
        if (Exporting) return;
        StopPlayback();
        if (Clips.Count > 0) PushUndo("clear all");             // (can be undone, so no question)
        RemoveAllClips();
    }

    // ------------------------------------------------------------------ export
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoText))]
    private bool exporting;

    public string GoText => Exporting ? "Abort" : "Export";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressCaption))]
    private double progress;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressCaption))]
    private string progressText = "";

    /// <summary>Text in the progress bar: the percentage while exporting, then "Done in ..." / "Failed" / "Aborted".</summary>
    public string ProgressCaption => Exporting ? Core.Py.Py.F(Progress, 0) + "%" : ProgressText;

    partial void OnExportingChanged(bool value) => OnPropertyChanged(nameof(ProgressCaption));
    [ObservableProperty] private ProgressLook progressLook = ProgressLook.Normal;
    [ObservableProperty] private string exportMessage = "";
    [ObservableProperty] private string? outPath;
    Encoder? encoder;

    string? InitDir() => SaveDir.Trim() is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    Preset? PresetByName(string n) => presets.FirstOrDefault(p => p.Name == n);

    [RelayCommand]
    void GoOrAbort()
    {
        if (Exporting) encoder?.Cancel();
        else StartExport();
    }

    /// <summary>The Export button (reference start_export).</summary>
    public void StartExport()
    {
        if (Exporting) return;
        StopPlayback();
        if (Clips.Count == 0) { Dialogs.Info("No clips", "Add at least one video first."); return; }
        if (S.ToExportSettings(false, out var error) is null)
        {
            Dialogs.Error(S.Bitrate.Trim().Length > 0 ? "Invalid bitrate" : "Invalid size", error!);
            return;
        }
        var rows = Clips.Select(c => c.Row).ToList();
        var clips = ExportPlanner.PrepareClips(S, rows);
        var inputs = ExportPlanner.InputSet(clips);
        bool join = false;
        if (clips.Count > 1)
        {
            int pick = Dialogs.Choice("Export", $"{clips.Count} clips are in the timeline. How do you want to export them?",
                                      ["Join into one video", "Each clip as its own file", "Cancel"], S.Mode == "join" ? 0 : 1, 2);
            if (pick is not (0 or 1)) return;
            S.Mode = pick == 0 ? "join" : "separate";
            join = pick == 0;
        }
        var st = S.ToExportSettings(join, out _)!;
        string? firstDir = Path.GetDirectoryName(clips[0].Path);
        List<ExportJob> jobs;
        if (join || clips.Count == 1)
        {
            string stem = ExportPlanner.Stem(clips[0].Path);
            var names = ExportPlanner.OneFileName(S, st, rows, clips, join, PresetByName);
            string? output = InitDir() is { } d ? ExportPlanner.AutoPath(S, d, stem, inputs, null, names)
                : Dialogs.SaveFile(join ? "Save joined video as" : "Save as", S.OutStem(stem, names) + ".mp4", firstDir);
            if (output is null) return;
            if (inputs.Contains(Path.GetFullPath(output).ToLowerInvariant()))
            {
                Dialogs.Error("Same file", "Choose a different output file name.");
                return;
            }
            jobs = [ExportPlanner.OneFile(S, st, rows, clips, output, join, PresetByName)];
        }
        else
        {
            string? folder = InitDir() ?? Dialogs.PickFolder("Choose a folder for the exported files", firstDir);
            if (folder is null) return;
            jobs = ExportPlanner.Separate(S, st, rows, clips, folder, PresetByName);
        }
        RunExport(jobs, st);
    }

    void RunExport(List<ExportJob> jobs, ExportSettings st)
    {
        var runner = new ProcessRunner(p => Ui(() => Progress = p), SetExportMessage);
        encoder = new Encoder(runner, new RealFileSystem(), p => Ui(() => Progress = p), SetExportMessage,
                              Text.InterpKey(st.SlowInterp) == "rife" ? AppPaths.FindRife(Tools) : null, runner,
                              m => Debug.WriteLine(m)) { Ffmpeg = Tools.Ffmpeg ?? "ffmpeg", UseHwDec = cudaOk };
        var enc = encoder;
        Exporting = true;
        OutPath = null;
        ProgressLook = ProgressLook.Normal;
        Progress = 0;
        ProgressText = "";
        ExportMessage = "Starting...";
        Task.Run(() =>
        {
            var done = new List<(string Out, long Size)>();
            var errors = new List<string>();
            var t0 = Stopwatch.StartNew();
            string? current = null;
            try
            {
                for (int k = 0; k < jobs.Count; k++)
                {
                    var job = jobs[k];
                    current = job.Out;
                    string tag = jobs.Count > 1 ? $"[{k + 1}/{jobs.Count}] " : "";
                    try
                    {
                        long size = enc.Encode(job.Out, job.Clips, job.Settings, 100.0 * k / jobs.Count, 100.0 / jobs.Count, tag);
                        done.Add((job.Out, size));
                    }
                    catch (Exception e) when (e is not ExportCancelledException)
                    {
                        errors.Add($"{Path.GetFileName(job.Out)}: {e.Message}");
                        try { File.Delete(job.Out); } catch (IOException) { }
                    }
                }
                var (_, line) = ExportPlanner.Report(jobs, st, done, errors);
                string took = Text.FmtElapsed(t0.Elapsed.TotalSeconds);
                Ui(() =>
                {
                    ExportMessage = line;
                    (errors.Count > 0 ? SystemSounds.Exclamation : SystemSounds.Asterisk).Play();
                    if (done.Count > 0)
                    {
                        Progress = 100;
                        ProgressLook = ProgressLook.Done;
                        ProgressText = $"Done in {took}";
                        OutPath = done[^1].Out;
                    }
                    else
                    {
                        ProgressLook = ProgressLook.Failed;
                        ProgressText = $"Failed after {took}";
                    }
                });
            }
            catch (ExportCancelledException)
            {
                if (current is not null) try { File.Delete(current); } catch (IOException) { }
                Ui(() =>
                {
                    ExportMessage = "The export was stopped - the unfinished file was deleted";
                    ProgressLook = ProgressLook.Failed;
                    ProgressText = "Aborted";
                });
            }
            catch (Exception e)
            {
                string first = e.Message.Trim().Split('\n').FirstOrDefault() ?? "unknown error";
                Ui(() =>
                {
                    ExportMessage = "Export failed - " + (first.Length > 160 ? first[..160] : first);
                    ProgressLook = ProgressLook.Failed;
                    ProgressText = "Failed";
                    Dialogs.Error("Export failed", e.Message);
                });
            }
            finally
            {
                Ui(() => { Exporting = false; encoder = null; });
            }
        });
    }

    void SetExportMessage(string text) => Ui(() => ExportMessage = text);

    [RelayCommand]
    void OpenOutputFolder()
    {
        if (OutPath is not { } p) return;
        try
        {
            if (File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{Path.GetFullPath(p)}\"");
            else if (Path.GetDirectoryName(p) is { } d) Process.Start(new ProcessStartInfo(d) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e) { Dialogs.Toast($"Could not open the folder: {e.Message}"); }
    }

    // ------------------------------------------------------------------ closing
    /// <summary>settings.json on close (reference save_all_settings): the export settings plus the window state.</summary>
    public void SaveSettings(string? geometry, bool zoomed, int? sash)
    {
        settings["theme"] = Theme;
        S.WriteTo(settings);
        settings["sound"] = SoundOn;
        settings["pv_vol"] = PvVol;
        settings["zoomed"] = zoomed;
        settings["restore_session"] = RestoreSession;
        settings["save_dir"] = SaveDir;
        if (soundListH is { } slh) settings["sound_list_h"] = slh;
        SaveModuleSettings();
        settings["keys"] = new JsonObject(Keys.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value))));
        if (geometry is not null) settings["geometry"] = geometry;
        if (sash is not null) settings["sash"] = sash;
        Directory.CreateDirectory(AppPaths.LibraryDir);
        SettingsStore.Save(AppPaths.SettingsFile, settings);
    }

    /// <summary>With clips open: Save / Close without saving / Cancel (reference on_close). A running export is stopped.</summary>
    public bool CanClose()
    {
        if (Clips.Count > 0)
        {
            int n = Clips.Count;
            int pick = Dialogs.Choice("Close Clip Shrinker",
                $"{n} clip{(n != 1 ? "s are" : " is")} open in the timeline." + (Exporting ? " An export is running." : ""),
                ["Save", "Close without saving", "Cancel"], 0, 2);
            if (pick is not (0 or 1)) return false;
            if (pick == 0 && !SaveProject(false)) return false;
        }
        encoder?.Cancel();
        return true;
    }
}
