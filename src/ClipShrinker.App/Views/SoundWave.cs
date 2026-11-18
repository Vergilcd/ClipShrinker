using System.IO;
using System.Windows;
using System.Windows.Media;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>2.7: waveform peaks per sound file (50 per second), loaded once in the background and shared by the
/// Sounds lanes and the Sounds list.</summary>
public static class SoundPeakCache
{
    static readonly Dictionary<string, float[]?> peaks = new(StringComparer.OrdinalIgnoreCase);
    public static event Action? Loaded;

    /// <summary>The peaks, or null while they load (Loaded fires when they are ready).</summary>
    public static float[]? Get(string path)
    {
        lock (peaks)
        {
            if (peaks.TryGetValue(path, out var p)) return p;
            peaks[path] = null;
        }
        var tools = (Application.Current?.MainWindow?.DataContext as MainViewModel)?.Tools;
        Task.Run(() =>
        {
            float[] p = [];
            try { if (tools is not null) p = tools.SoundPeaks(path); }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            lock (peaks) peaks[path] = p;
            Loaded?.Invoke();
        });
        return null;
    }
}

/// <summary>The faded waveform of a whole sound file behind its entry in the Sounds list.</summary>
public sealed class SoundWave : FrameworkElement
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(nameof(Path), typeof(string), typeof(SoundWave),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Path { get => (string?)GetValue(PathProperty); set => SetValue(PathProperty, value); }

    static readonly Brush Wave = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public SoundWave()
    {
        IsHitTestVisible = false;
        void OnLoaded() => Dispatcher.BeginInvoke(InvalidateVisual);
        Loaded += (_, _) => SoundPeakCache.Loaded += OnLoaded;
        Unloaded += (_, _) => SoundPeakCache.Loaded -= OnLoaded;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (Path is not { } p || W < 4 || H < 2 || SoundPeakCache.Get(p) is not { Length: > 0 } pk) return;
        double mid = H / 2, amp = (H - 2) / 2;
        for (double x = 0; x < W; x += 2)
        {
            int a = (int)(x / W * pk.Length), b = Math.Max(a + 1, (int)((x + 2) / W * pk.Length));
            float m = 0;
            for (int i = a; i < b && i < pk.Length; i++) m = Math.Max(m, pk[i]);
            double h = Math.Max(0.5, m * amp);
            dc.DrawRectangle(Wave, null, new Rect(x, mid - h, 1.2, 2 * h));
        }
    }
}
