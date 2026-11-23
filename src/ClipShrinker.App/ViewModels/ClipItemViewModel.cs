using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipShrinker.App.ViewModels;

/// <summary>One clip card: the timeline editor, its thumbnails (several levels of detail, only the visible part is
/// made), selection state and the bitrate preset (reference ClipRow without the drawing).</summary>
public sealed partial class ClipItemViewModel : ObservableObject, IThumbOwner
{
    const int MaxThumbCache = 800;

    readonly ThumbnailService thumbs;
    readonly Func<bool> hdr;
    readonly DispatcherTimer thumbTimer = new();

    public TimelineClip Row { get; }
    public TimelineEditor Editor { get; }
    public string Path => Row.Path;
    public string Name => System.IO.Path.GetFileName(Row.Path);

    /// <summary>Thumbnails by (level, index, n1).</summary>
    public Dictionary<(int L, int K, int N1), BitmapSource> Cache { get; } = [];
    int n1, resetId, pending;
    volatile int token;                // read by the worker threads: a newer request stops older ones
    double width = 800;

    /// <summary>Something on the timeline changed: redraw (and the main window updates the summary).</summary>
    public event Action? Changed;

    public ClipItemViewModel(TimelineClip row, ThumbnailService thumbs, Func<bool> hdr)
    {
        Row = row;
        this.thumbs = thumbs;
        this.hdr = hdr;
        Editor = TimelineEditor.EdgeToEdge(row);                // thumbnails edge to edge (2.0 look)
        Editor.ViewChanged = () =>
        {
            OnPropertyChanged(nameof(ZoomLabel));
            OnPropertyChanged(nameof(IsZoomed));
            OnPropertyChanged(nameof(ZoomChipText));
            thumbTimer.Stop();
            thumbTimer.Interval = TimeSpan.FromMilliseconds(180);
            thumbTimer.Start();
        };
        thumbTimer.Tick += (_, _) => { thumbTimer.Stop(); RefreshThumbs(); };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(TitleName))]
    private int index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Highlighted))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(TitleName))]
    [NotifyPropertyChangedFor(nameof(TimelineHeight))]
    private bool isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Highlighted))]
    private bool isSelected;

    partial void OnIsActiveChanged(bool value)
    {
        Editor.Active = value;
        if (!value) Editor.Sel = null;
        NotifyChanged();
    }

    /// <summary>Active or selected: accent title bar and ring.</summary>
    public bool Highlighted => IsActive || IsSelected;
    public double TimelineHeight => Editor.Height;
    /// <summary>2.7: height of the Sounds lane under the thumbnails (Sound track module): full on the active clip, a thin
    /// strip on others that have sounds, none otherwise.</summary>
    public double SoundLaneHeight => !Core.Modules.Features.SoundTrack ? 0 : Editor.Active ? 40 : Row.Sounds.Count > 0 ? 8 : 0;
    public string Title => Editor.Title(Index);
    public string TitleName => Editor.TitleName(Index);
    public string TitleInfo => Editor.TitleInfo;
    public string ZoomLabel => Editor.ZoomLabel;
    /// <summary>2.9.1: the title bar's zoom chip shows while the timeline is zoomed in ("Zoom ×2.4").</summary>
    public bool IsZoomed => Editor.ZoomLabel.Length > 0;
    public string ZoomChipText => IsZoomed ? "Zoom ×" + Editor.ZoomFactor.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "";

    // thumbnails are served active clip first, then in list order
    bool IThumbOwner.ThumbActive => isActiveForThumbs;
    int IThumbOwner.ThumbIndex => indexForThumbs;
    volatile bool isActiveForThumbs;
    volatile int indexForThumbs;
    partial void OnIndexChanged(int value) => indexForThumbs = value;

    public void NotifyChanged()
    {
        isActiveForThumbs = IsActive;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(TitleName));
        OnPropertyChanged(nameof(TitleInfo));
        OnPropertyChanged(nameof(ZoomLabel));
        OnPropertyChanged(nameof(IsZoomed));
        OnPropertyChanged(nameof(ZoomChipText));
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ bitrate preset
    public List<string> PresetLabels { get; private set; } = ["Auto bitrate"];
    List<Core.Settings.Preset> presets = [];

    public void SetPresets(List<Core.Settings.Preset> list)
    {
        presets = list;
        if (Row.Preset is { } n && presets.All(p => p.Name != n)) Row.Preset = null;   // deleted preset -> Auto
        PresetLabels = ["Auto bitrate", .. presets.Select(Label)];
        OnPropertyChanged(nameof(PresetLabels));
        OnPropertyChanged(nameof(PresetLabel));
    }

    static string Label(Core.Settings.Preset p) => $"{p.Name} ({Core.Py.Py.G(p.Mbps)} Mbps)";

    public string PresetLabel
    {
        get => presets.FirstOrDefault(p => p.Name == Row.Preset) is { } p ? Label(p) : "Auto bitrate";
        set
        {
            int i = PresetLabels.IndexOf(value);
            string? name = i <= 0 ? null : presets[i - 1].Name;
            if (name == Row.Preset) return;
            Editor.PushUndo("bitrate preset");
            Row.Preset = name;
            OnPropertyChanged();
        }
    }

    public void SetPreset(string? name)
    {
        Row.Preset = presets.Any(p => p.Name == name) ? name : null;
        OnPropertyChanged(nameof(PresetLabel));
    }

    // ------------------------------------------------------------------ thumbnails
    /// <summary>The timeline got a new width: after a short pause fill the gaps (reference _sched_thumbs).</summary>
    public void SetWidth(double w)
    {
        width = w;
        if (n1 == 0) { StartThumbs(); return; }
        int want = Editor.WantN1(width);
        if (want <= n1 && want >= 0.6 * n1) return;
        thumbTimer.Stop();
        thumbTimer.Interval = TimeSpan.FromMilliseconds(IsActive ? 250 : 700);
        thumbTimer.Start();
    }

    /// <summary>Throws away all thumbnails and makes the visible ones again (new clip, HDR switch).</summary>
    public void StartThumbs()
    {
        resetId++;
        Cache.Clear();
        n1 = Editor.WantN1(width);
        RefreshThumbs();
    }

    public void RefreshThumbs()
    {
        token++;
        if (n1 == 0) return;
        int want = Editor.WantN1(width);
        if (want > n1 || want < 0.6 * n1) n1 = want;          // window got wider / much narrower: other spacing
        int L = Editor.Level, nn = n1;
        var need = Editor.NeededThumbs(nn, k => Cache.ContainsKey((L, k, nn)));
        pending = need.Count;
        if (need.Count == 0) { DropStale(); return; }
        string? trc = hdr() ? Row.Info.Trc : null;
        int tok = token, rid = resetId, tw = Editor.ThumbWidth;
        int size = IsActive ? Math.Max(3, Math.Min(Core.C.ThumbBatch, (int)Math.Ceiling(need.Count / (double)thumbs.Workers))) : Core.C.ThumbBatch;
        var chunks = need.Chunk(size).ToList();
        var disp = Application.Current.Dispatcher;
        isActiveForThumbs = IsActive;
        thumbs.Submit(this, chunks.Select(ch => (Action)(() =>
        {
            var times = ch.Select(k => Editor.ThumbTime(L, k, nn)).ToList();
            ThumbnailService.GrabFrames(Row.Path, times, tw, (int)Editor.ThumbHeight, trc, () => tok != token, (i, png) =>
            {
                var bmp = Decode(png);
                if (bmp is not null) disp.BeginInvoke(() => AddThumb(rid, (L, ch[i], nn), bmp));
            });
        })));
    }

    static BitmapSource? Decode(byte[] png)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(png);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException) { return null; }
    }

    void AddThumb(int rid, (int L, int K, int N1) key, BitmapSource bmp)
    {
        if (rid != resetId) return;
        if (Cache.Count > MaxThumbCache)
        {
            int lv = Editor.Level;
            foreach (var k in Cache.Keys.Where(k => k.L != 0 && k.L != lv).ToList()) Cache.Remove(k);
        }
        Cache[key] = bmp;
        if (key.N1 == n1 && --pending <= 0) DropStale();
        Changed?.Invoke();
    }

    /// <summary>The first visible thumbnails of this clip are in (once; for the "Loading videos" popup).</summary>
    public event Action? FirstThumbsReady;
    bool firstThumbsDone;
    public bool FirstThumbsDone => firstThumbsDone;

    /// <summary>Thumbnails made for another window width are removed once the new ones are in.</summary>
    void DropStale()
    {
        if (!firstThumbsDone) { firstThumbsDone = true; FirstThumbsReady?.Invoke(); }
        foreach (var k in Cache.Keys.Where(k => k.N1 != n1).ToList()) Cache.Remove(k);
    }

    /// <summary>Undo / redo: trims, sections, preset and zooms as they were (reference set_state).</summary>
    public void ApplyState(Core.Project.ClipState st)
    {
        st.Apply(Row);
        Editor.Sel = null;
        Editor.ZSel = null;
        Editor.Playhead = Math.Min(Math.Max(Editor.Playhead, Row.Start), Row.End);
        if (Row.Preset is { } n && presets.All(p => p.Name != n)) Row.Preset = null;
        OnPropertyChanged(nameof(PresetLabel));
        NotifyChanged();
    }

    /// <summary>The preset list changed: renamed presets follow, deleted ones turn back to Auto.</summary>
    public void PresetRenamed(string oldName, string newName)
    {
        if (Row.Preset == oldName) Row.Preset = newName;
    }

    /// <summary>The clip came back through undo: make its missing thumbnails again.</summary>
    public void Reattach()
    {
        resetId++;
        RefreshThumbs();
    }

    /// <summary>The clip left the list: stop making thumbnails for it.</summary>
    public void Detach()
    {
        thumbs.Drop(this);
        token++;
        thumbTimer.Stop();
    }

    /// <summary>A small thumbnail for the drag ghost.</summary>
    public BitmapSource? FirstThumb => Cache.OrderBy(k => k.Key).Select(k => k.Value).FirstOrDefault();
}
