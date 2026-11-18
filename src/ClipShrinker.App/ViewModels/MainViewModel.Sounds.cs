using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ClipShrinker.Core;
using ClipShrinker.Core.Modules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipShrinker.App.ViewModels;

/// <summary>A sound file in the library folder (modules\sound-track\sounds).</summary>
public sealed record LibrarySound(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>2.7 Sound track module: the sound library, adding sounds to a clip's Sounds lane, and the settings of the
/// selected sound. The lane itself (drawing, dragging) is Views.SoundLaneView.</summary>
public sealed partial class MainViewModel
{
    public bool SoundModuleOn => ModuleOn(ModuleIds.SoundTrack);

    public static string SoundFolder => Path.Combine(AppPaths.ModuleDir(ModuleIds.SoundTrack), "sounds");

    public ObservableCollection<LibrarySound> SoundLibrary { get; } = [];

    /// <summary>The library sound the Sound key adds (highlighted in the list: the last one picked).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoundListHint))]
    private LibrarySound? currentLibrarySound;

    public string SoundListHint => SoundLibrary.Count == 0
        ? "No sounds yet: click \"Add files...\" or put sound files in the folder (\"Open folder\"). You can also drop sound files from Explorer straight onto a clip's Sounds lane."
        : $"Drag a sound onto a clip's Sounds lane, or click + to add it at the playhead. {Text.ChordLabel(Keys["sound"])} adds the highlighted one.";

    int? soundListH;

    /// <summary>Height of the Sounds list (dragged with the grip at the bottom of the drop-down; kept in settings).</summary>
    public int SoundListHeight
    {
        get => soundListH ??= Math.Clamp(Int("sound_list_h") ?? 260, 120, 1200);
        set { soundListH = Math.Clamp(value, 120, 1200); OnPropertyChanged(); }
    }

    /// <summary>Creates modules\sound-track\sounds when the module is on, so the folder is there to fill from the start.</summary>
    static void EnsureSoundFolder()
    {
        try { Directory.CreateDirectory(SoundFolder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads the library folder again (when the Sounds list opens).</summary>
    public void RefreshSoundLibrary()
    {
        string? keep = CurrentLibrarySound?.Path;
        SoundLibrary.Clear();
        try
        {
            if (Directory.Exists(SoundFolder))
                foreach (var f in Directory.EnumerateFiles(SoundFolder).Where(Sounds.IsSoundFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    SoundLibrary.Add(new LibrarySound(f));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        CurrentLibrarySound = SoundLibrary.FirstOrDefault(s => s.Path == keep) ?? SoundLibrary.FirstOrDefault();
        OnPropertyChanged(nameof(SoundListHint));
    }

    [RelayCommand]
    void OpenSoundFolder()
    {
        try
        {
            Directory.CreateDirectory(SoundFolder);
            Process.Start(new ProcessStartInfo(SoundFolder) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Dialogs.Warn("Sound folder", e.Message);
        }
    }

    /// <summary>Copies sound files into the library folder (so the list keeps them).</summary>
    public void ImportSounds(IEnumerable<string> files)
    {
        try
        {
            Directory.CreateDirectory(SoundFolder);
            foreach (var f in files.Where(Sounds.IsSoundFile))
            {
                string dst = Path.Combine(SoundFolder, Path.GetFileName(f));
                if (!string.Equals(Path.GetFullPath(f), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(f, dst, true);
                CurrentLibrarySound = new LibrarySound(dst);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Dialogs.Warn("Add sounds", e.Message); }
        string? cur = CurrentLibrarySound?.Path;
        RefreshSoundLibrary();
        CurrentLibrarySound = SoundLibrary.FirstOrDefault(s => s.Path == cur) ?? CurrentLibrarySound;
    }

    readonly Dictionary<string, double> soundDurations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a sound file to a clip's Sounds lane at clip time t (one undo step). False when it is not a sound.</summary>
    public bool AddSoundAt(ClipItemViewModel row, string path, double t)
    {
        if (Exporting || !SoundModuleOn || !Clips.Contains(row)) return false;
        if (!soundDurations.TryGetValue(path, out double dur))
        {
            try { dur = Tools.ProbeSoundDuration(path); }
            catch (Exception e) when (e is Core.Media.MediaException or IOException or System.Text.Json.JsonException)
            {
                Dialogs.Warn("Not a sound file", $"{Path.GetFileName(path)} could not be read as sound:\n{e.Message}");
                return false;
            }
            soundDurations[path] = dur;
        }
        StopPlayback();
        PushUndo("add sound");
        var b = new SoundBlock { Path = path, At = Math.Max(0, Math.Min(t, row.Editor.Dur - Sounds.MinLen)), Len = dur, FileDur = dur };
        row.Row.Sounds.Add(b);
        SetActive(row);
        SelectSound(row, b);
        row.NotifyChanged();
        return true;
    }

    /// <summary>The + on a sound in the list: that sound at the active clip's playhead (it becomes the highlighted one).</summary>
    [RelayCommand]
    void AddLibrarySound(LibrarySound? s)
    {
        if (s is null) return;
        CurrentLibrarySound = s;
        AddCurrentSound();
    }

    /// <summary>The Sound key: the highlighted library sound at the active clip's playhead.</summary>
    public bool AddCurrentSound()
    {
        if (Exporting || !SoundModuleOn || TargetClip is not { } row) return false;
        if (CurrentLibrarySound is null) RefreshSoundLibrary();
        if (CurrentLibrarySound is not { } s)
        {
            Dialogs.Toast("No sound picked: add sound files in Sounds, or drop one on a clip's Sounds lane");
            return true;
        }
        return AddSoundAt(row, s.Path, row.Editor.Playhead);
    }

    // ------------------------------------------------------------------ the selected sound
    public ClipItemViewModel? SelectedSoundClip { get; private set; }
    public SoundBlock? SelectedSound { get; private set; }

    public void SelectSound(ClipItemViewModel? row, SoundBlock? b)
    {
        (SelectedSoundClip, SelectedSound) = (b is null ? null : row, b);
        OnPropertyChanged(nameof(SelectedSound));
        OnPropertyChanged(nameof(HasSelectedSound));
        OnPropertyChanged(nameof(SelectedSoundName));
        OnPropertyChanged(nameof(SoundVolume));
        OnPropertyChanged(nameof(SoundVolumeText));
        OnPropertyChanged(nameof(SoundFade));
        OnPropertyChanged(nameof(SoundFadeText));
    }

    public bool HasSelectedSound => SelectedSound is not null && SelectedSoundClip is { } c && c.Row.Sounds.Contains(SelectedSound);
    public string SelectedSoundName => SelectedSound?.Name ?? "";

    bool soundEditPushed;                                      // one undo step per slider drag

    /// <summary>A setting of the selected sound changes (the preview sound follows).</summary>
    void EditSound(Action<SoundBlock> change)
    {
        if (!HasSelectedSound || SelectedSound is not { } b || SelectedSoundClip is not { } row) return;
        if (!soundEditPushed) { PushUndo("sound setting"); soundEditPushed = true; }
        change(b);
        row.NotifyChanged();
        PreviewSettingsChanged();
    }

    /// <summary>The slider was let go: the next change is a new undo step.</summary>
    public void EndSoundEdit() => soundEditPushed = false;

    public int SoundVolume
    {
        get => SelectedSound?.VolumeDb ?? 0;
        set { EditSound(b => b.VolumeDb = Sounds.ClampVolume(value)); OnPropertyChanged(); OnPropertyChanged(nameof(SoundVolumeText)); }
    }

    public string SoundVolumeText => Equalizer.FormatDb(SoundVolume);

    /// <summary>Fade in / out in tenths of a second (the slider), 0 .. 50.</summary>
    public int SoundFade
    {
        get => (int)Math.Round((SelectedSound?.Fade ?? 0) * 10);
        set { EditSound(b => b.Fade = Sounds.ClampFade(value / 10.0, b.Len)); OnPropertyChanged(); OnPropertyChanged(nameof(SoundFadeText)); }
    }

    public string SoundFadeText => $"{(SelectedSound?.Fade ?? 0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s";

    [RelayCommand]
    void RemoveSelectedSound() => RemoveSound(SelectedSoundClip, SelectedSound);

    /// <summary>Takes a sound off its lane (one undo step). False when there was none.</summary>
    public bool RemoveSound(ClipItemViewModel? row, SoundBlock? b)
    {
        if (Exporting || row is null || b is null || !row.Row.Sounds.Contains(b)) return false;
        StopPlayback();
        PushUndo("remove sound");
        row.Row.Sounds.Remove(b);
        if (ReferenceEquals(SelectedSound, b)) SelectSound(null, null);
        row.NotifyChanged();
        return true;
    }

    /// <summary>A sound on a lane was moved or trimmed (the drag itself pushed the undo step).</summary>
    public void SoundMoved(ClipItemViewModel row)
    {
        row.NotifyChanged();
        OnPropertyChanged(nameof(SoundFadeText));
    }

    // ------------------------------------------------------------------ listening to a library sound
    System.Media.SoundPlayer? player;

    /// <summary>Plays a library sound once (decoded to a temporary WAV, which SoundPlayer can play).</summary>
    [RelayCommand]
    void PlayLibrarySound(LibrarySound? s)
    {
        if (s is null || Tools.Ffmpeg is null) return;
        player?.Stop();
        string wav = Path.Combine(Path.GetTempPath(), $"clipshrinker_{Environment.ProcessId}_sample.wav");
        Task.Run(() =>
        {
            int code = Core.Media.Tools.Capture([Tools.Ffmpeg, "-y", "-v", "error", "-i", s.Path, "-t", "30", "-ac", "2", "-ar", "44100",
                                                  "-c:a", "pcm_s16le", wav]).Code;
            if (code != 0) return;
            Ui(() =>
            {
                try { player = new System.Media.SoundPlayer(wav); player.Play(); }
                catch (Exception e) when (e is IOException or InvalidOperationException) { }
            });
        });
    }
}
