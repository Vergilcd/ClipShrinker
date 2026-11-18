using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipShrinker.App.Media;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipShrinker.App.ViewModels;

/// <summary>The preview: still frames, playback with sound, the zoom bracket and the effect sound samples
/// (reference request_preview, start_playback / stop_playback, bracket_refresh, play_effect_sample).</summary>
public sealed partial class MainViewModel
{
    PreviewService? previewSvc;
    PreviewService Preview => previewSvc ??= CreatePreview();
    (ClipItemViewModel Row, double T)? lastPreview;
    ClipItemViewModel? playRow;

    PreviewService CreatePreview()
    {
        var p = new PreviewService(Application.Current.Dispatcher) { SoundOn = SoundOn };
        p.StillReady += img => { PreviewImage = img; BracketChanged?.Invoke(); };
        p.FrameShown += t =>
        {
            if (PreviewImage != p.Frame) PreviewImage = p.Frame;
            if (playRow is { } r && Clips.Contains(r)) { r.Editor.FollowPlayhead(t); r.NotifyChanged(); }
        };
        p.PlaybackFailed += () => { StopPlayback(); Dialogs.Toast("Could not play the preview"); };
        p.PlaybackEnded += StopPlayback;                      // played once outside the trim: no loop
        return p;
    }

    /// <summary>The picture shown in the preview (a still frame or the playback bitmap); null = the hint text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private ImageSource? previewImage;

    public bool HasPreview => PreviewImage is not null;

    /// <summary>Size of the preview box in pixels (set by the window).</summary>
    public (double W, double H) PreviewBox { get; set; } = (640, 360);

    /// <summary>The zoom bracket should be redrawn (a new frame, the playhead moved, an area changed).</summary>
    public event Action? BracketChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayIcon))]
    private bool playing;

    public string PlayIcon => Playing ? "" : "";                // pause / play (Segoe Fluent Icons)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeakerIcon))]
    private bool soundOn = true;

    public string SpeakerIcon => SoundOn ? "" : "";

    [ObservableProperty] private int pvVol = 100;

    partial void OnPvVolChanged(int value)
    {
        WaveOut.Volume = Math.Max(0, Math.Min(100, value)) / 100.0;
        previewSvc?.VolumeChanged();
    }

    void InitPreviewSettings()
    {
        SoundOn = Bool("sound") ?? true;
        PvVol = Int("pv_vol") is { } v ? Math.Max(0, Math.Min(200, v)) : 100;
        WaveOut.Volume = Math.Max(0, Math.Min(100, PvVol)) / 100.0;
    }

    /// <summary>The preview is shown full screen (a larger frame is made).</summary>
    public bool FullScreen { get; set; }

    (int W, int H) PvDims() => PreviewPlan.Dims(PreviewBox.W, PreviewBox.H, FullScreen);

    /// <summary>Shows the frame of row at t (and makes row the active clip).</summary>
    public void RequestPreview(ClipItemViewModel row, double t)
    {
        lastPreview = (row, t);
        SetActive(row);
        var (w, h) = PvDims();
        Preview.RequestStill(row.Path, t, PreviewPlan.Crop(row.Row.Info, S.Ratio), w, h, S.Hdr ? row.Row.Info.Trc : null, S.PictureFilter());
        BracketChanged?.Invoke();
    }

    /// <summary>The preview box changed size: the frame again at the new size (playback restarts).</summary>
    public void PreviewResized()
    {
        if (Playing) StartPlayback();
        else if (lastPreview is { } lp && Clips.Contains(lp.Row)) RequestPreview(lp.Row, lp.T);
    }

    /// <summary>Crop / HDR changed: restart playback or show the frame again.</summary>
    void PreviewSettingsChanged()
    {
        if (Playing) StartPlayback();
        else if (lastPreview is { } lp && Clips.Contains(lp.Row)) RequestPreview(lp.Row, lp.T);
    }

    void ClearPreview()
    {
        StopPlayback();
        lastPreview = null;
        PreviewImage = null;
    }

    [RelayCommand]
    public void TogglePlay()
    {
        if (Playing) StopPlayback();
        else StartPlayback();
    }

    public void StartPlayback()
    {
        if (Exporting || Clips.Count == 0) return;
        var row = ActiveClip is { } a && Clips.Contains(a) ? a : Clips[0];
        StopPlayback();
        SetActive(row);
        var i = row.Row.Info;
        var (w, h) = PreviewPlan.PlaySize(i, S.Ratio, PvDims());
        var ed = row.Editor;
        var secs = ed.Vis().Select(v => new SpeedSection(v.A, v.B, v.Sec.Speed)).ToList();
        var zooms = ed.VisZ().Select(v => new Zoom(v.A, v.B, v.Z.X, v.Z.Y, v.Z.W)).ToList();
        var clip = row.Row;
        var (t0, end, once) = PreviewPlan.PlayRange(clip, ed.Playhead);
        bool hasSounds = SoundModuleOn && clip.Sounds.Count > 0;           // (2.7: a silent clip can have sounds)
        var req = new PlayRequest(row.Path, clip.Start, end, t0, PreviewPlan.Crop(i, S.Ratio),
                                  w, h, PreviewPlan.PlayFps(i), SoundOn && (i.Audio || hasSounds), S.Hdr ? i.Trc : null, secs,
                                  PreviewPlan.Effects(S, zooms, SoundModuleOn ? clip.Sounds : null), !i.Audio ? 0 : S.MuteMic ? 1 : i.NAudio,
                                  () => Clips.Contains(row) ? (clip.Start, clip.End) : null, once);
        playRow = row;
        Playing = true;
        Preview.Play(req);
        // 2.2.1: the still frame stays up until the first playback frame is drawn (FrameShown swaps the picture);
        // showing the new, still empty bitmap at once flashed black while ffmpeg seeks (~0.2 s).
        BracketChanged?.Invoke();
    }

    public void StopPlayback()
    {
        bool was = Playing;
        Playing = false;
        previewSvc?.Stop();
        if (was && ActiveClip is { } row && Clips.Contains(row))
        {
            if (row.Editor.ZSel is not null) RequestPreview(row, row.Editor.Playhead);   // unzoomed frame under the bracket
            BracketChanged?.Invoke();
        }
    }

    [RelayCommand]
    public void ToggleMute()
    {
        SoundOn = !SoundOn;
        if (Playing)
        {
            if (previewSvc is { } p) p.SetMuted(!SoundOn);
            if (SoundOn && playRow is { } r && r.Row.Info.Audio && !(previewSvc?.SoundOn ?? false)) StartPlayback();
        }
        else if (previewSvc is { } p2) p2.SoundOn = SoundOn;
        Dialogs.Toast(SoundOn ? "Preview sound on" : "Preview sound muted");
    }

    // ------------------------------------------------------------------ zoom bracket
    /// <summary>The zoom section the bracket edits, or null when no bracket is shown (playing, no frame, playhead outside).</summary>
    public ZoomSection? BracketTarget =>
        Playing || PreviewImage is null || ActiveClip is not { } row || !Clips.Contains(row) ? null : ZoomArea.Target(row.Editor);

    public void BracketMoved() => ActiveClip?.NotifyChanged();

    // ------------------------------------------------------------------ effect sound samples
    int sampleGen;
    [ObservableProperty] private string? sampleBusy;          // the kind being prepared ("fast" / "slow" / "zoom")

    /// <summary>A short sample of the sped-up / slow-mo / zoom sound with the current settings.</summary>
    [RelayCommand]
    void PlayEffectSample(string kind)
    {
        StopPlayback();
        var row = ActiveClip ?? Clips.FirstOrDefault();
        string? path = null;
        double t0 = 0;
        if (row is not null && row.Row.Info.Audio)
        {
            double need = kind switch { "fast" => 5.8, "slow" => 3.2, _ => 3.4 };
            path = row.Path;
            var c = row.Row;
            t0 = c.Start <= row.Editor.Playhead && row.Editor.Playhead < c.End ? row.Editor.Playhead : c.Start;
            t0 = Math.Max(0.0, Math.Min(t0, c.Info.Dur - need));
        }
        int gen = ++sampleGen;
        SampleBusy = kind;
        string output = Path.Combine(Path.GetTempPath(), $"clipshrinker_{Environment.ProcessId}_sample{gen}.wav");
        var fx = PreviewPlan.Effects(S, []);
        Task.Run(() =>
        {
            bool ok = false;
            try { ok = Tools.Capture(PreviewCommands.EffectSample(kind, path, t0, output, fx)).Code == 0 && File.Exists(output); }
            catch (System.ComponentModel.Win32Exception) { }
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (SampleBusy == kind) SampleBusy = null;
                if (!ok) { Dialogs.Toast("Could not make the sound sample"); return; }
                if (gen != sampleGen) return;
                try { new SoundPlayer(output).Play(); } catch (Exception e) when (e is IOException or InvalidOperationException) { }
                Dialogs.Toast("Playing a sample: normal sound, then the effect, then normal again" +
                              (path is null ? " (no clip with sound loaded - a test melody is used)" : ""));
            });
        });
    }

    // ------------------------------------------------------------------ transition hover preview
    readonly Dictionary<string, List<BitmapSource>> transitionCache = [];

    /// <summary>The inputs of the transition preview: the first two clips as they would be joined, else the start and
    /// end of the first clip, else plain colours (reference _trans_sources).</summary>
    List<(string, double)>? TransitionSources()
    {
        if (Clips.Count >= 2)
        {
            var (a, b) = (Clips[0].Row, Clips[1].Row);
            return [(a.Path, Math.Max(a.Start, a.End - 1.2)), (b.Path, b.Start)];
        }
        if (Clips.Count == 1)
        {
            var r = Clips[0].Row;
            return [(r.Path, r.Start), (r.Path, Math.Max(r.Start, r.End - 1.2))];
        }
        return null;
    }

    /// <summary>About 1.6 s of the transition at 240 x 136 (cached). Runs ffmpeg: call it off the UI thread.</summary>
    public List<BitmapSource> TransitionFrames(string name, List<(string, double)>? src)
    {
        string key = name + "|" + string.Join(";", src?.Select(s => $"{s.Item1}@{s.Item2:R}") ?? []);
        lock (transitionCache) if (transitionCache.TryGetValue(key, out var hit)) return hit;
        var frames = new List<BitmapSource>();
        var raw = ThumbnailService.GrabFrame(PreviewCommands.TransitionPreview(name, src));
        int n = C.PvW * C.PvH * 3;
        if (raw is not null)
            for (int i = 0; i + n <= raw.Length; i += n)
            {
                var bmp = BitmapSource.Create(C.PvW, C.PvH, 96, 96, PixelFormats.Rgb24, null, raw.AsSpan(i, n).ToArray(), C.PvW * 3);
                bmp.Freeze();
                frames.Add(bmp);
            }
        if (frames.Count > 0) lock (transitionCache) transitionCache[key] = frames;
        return frames;
    }

    public List<(string, double)>? CurrentTransitionSources() => TransitionSources();

    /// <summary>The app closes: stop playback and delete the temporary sample sounds (reference cleanup_wavs).</summary>
    public void ShutdownPreview()
    {
        StopPlayback();
        foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), $"clipshrinker_{Environment.ProcessId}_*.wav"))
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
