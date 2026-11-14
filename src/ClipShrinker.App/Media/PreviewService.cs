using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Media;

namespace ClipShrinker.App.Media;

/// <summary>Everything a playback run needs; Trim() gives the current trim markers (they may be moved while it plays).
/// Once = play T0 .. End a single time and stop (the playhead was outside the trim), instead of looping the trim.</summary>
public sealed record PlayRequest(string Path, double Start, double End, double T0, (int W, int H)? Crop, int W, int H, double Fps,
                                 bool UseSound, string? Trc, IReadOnlyList<SpeedSection> Sections, EffectSettings Effects,
                                 int NAudio, Func<(double Start, double End)?> Trim, bool Once = false);

/// <summary>
/// The preview (reference request_preview / _preview_loop and start_playback / _play_loop / _deliver / _draw_frame):
/// still frames for clicks on the timeline, and real-time playback of the edited clip with sound.
/// </summary>
public sealed class PreviewService(Dispatcher ui)
{
    readonly object gate = new();
    (string Path, double T, (int W, int H)? Crop, int W, int H, string? Trc)? pendingStill;
    bool stillBusy;
    volatile int playGen;
    Process? playProc, audioProc;
    WaveOut? wave;
    int playLevel = 2;                      // 2 = GPU decode + scale, 1 = GPU decode, 0 = software (steps down on failure)

    public event Action<BitmapSource>? StillReady;
    /// <summary>A playback frame was drawn; the time is source seconds.</summary>
    public event Action<double>? FrameShown;
    public event Action? PlaybackFailed;
    /// <summary>A play-once run reached its end.</summary>
    public event Action? PlaybackEnded;
    public WriteableBitmap? Frame { get; private set; }
    public bool Playing { get; private set; }
    public bool SoundOn { get; set; } = true;

    // ------------------------------------------------------------------ still frames
    /// <summary>Asks for a frame at t; while one is being made only the newest request is kept.</summary>
    public void RequestStill(string path, double t, (int W, int H)? crop, int w, int h, string? trc)
    {
        lock (gate)
        {
            pendingStill = (path, t, crop, w, h, trc);
            if (stillBusy) return;
            stillBusy = true;
        }
        new Thread(StillLoop) { IsBackground = true, Name = "preview frame" }.Start();
    }

    void StillLoop()
    {
        while (true)
        {
            (string Path, double T, (int W, int H)? Crop, int W, int H, string? Trc) req;
            lock (gate)
            {
                if (pendingStill is not { } p) { stillBusy = false; return; }
                req = p;
                pendingStill = null;
            }
            var png = ThumbnailService.GrabFrame(PreviewCommands.GrabFrame(req.Path, req.T, req.W, req.H, req.Crop, req.Trc));
            if (png is null) continue;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(png);
                img.EndInit();
                img.Freeze();
                ui.BeginInvoke(() => { if (!Playing) StillReady?.Invoke(img); });
            }
            catch (NotSupportedException) { }
        }
    }

    // ------------------------------------------------------------------ playback
    public void Stop()
    {
        lock (gate)
        {
            playGen++;
            Playing = false;
            try { if (playProc is { HasExited: false }) playProc.Kill(); } catch (InvalidOperationException) { }
            StopWave();
        }
    }

    void StopWave()
    {
        var w = wave;
        wave = null;
        w?.Stop();
        try { if (audioProc is { HasExited: false }) audioProc.Kill(); } catch (InvalidOperationException) { }
        audioProc = null;
    }

    public void SetMuted(bool muted)
    {
        SoundOn = !muted;
        wave?.SetMuted(muted);
    }

    /// <summary>The volume slider moved: the playing sound follows at once.</summary>
    public void VolumeChanged() => wave?.ApplyVolume();

    public void Play(PlayRequest r)
    {
        Stop();
        int gen;
        lock (gate)
        {
            gen = playGen;
            Playing = true;
        }
        Frame = new WriteableBitmap(r.W, r.H, 96, 96, PixelFormats.Rgb24, null);
        new Thread(() =>
        {
            try { PlayLoop(gen, r); }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Debug.WriteLine("preview failed: " + e);
                if (gen == playGen) ui.BeginInvoke(() => PlaybackFailed?.Invoke());
            }
        }) { IsBackground = true, Name = "preview playback" }.Start();
    }

    static Process Start(IReadOnlyList<string> cmd)
    {
        var psi = new ProcessStartInfo(cmd[0])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
        var p = Process.Start(psi)!;
        p.StandardInput.Close();
        _ = p.StandardError.ReadToEndAsync();                 // drain stderr so ffmpeg never blocks on it
        return p;
    }

    /// <summary>Streams the audio of [a, end] through waveOut, paused until the first frame. False = not possible.</summary>
    bool WaveBegin(int gen, PlayRequest r, double a, double end)
    {
        WaveOut player;
        try { player = new WaveOut(); }
        catch (Exception e) when (e is IOException or DllNotFoundException) { return false; }
        var pieces = Timing.BuildPieces(a, end, r.Sections);
        var proc = Start(PreviewCommands.PreviewAudio(r.Path, a, end - a, pieces, true, r.NAudio, null, r.Effects));
        lock (gate)
        {
            if (gen != playGen) { try { proc.Kill(); } catch (InvalidOperationException) { } player.Stop(); return false; }
            StopWave();
            (audioProc, wave) = (proc, player);
        }
        if (!SoundOn) player.SetMuted(true);
        player.Feed(proc.StandardOutput.BaseStream);
        return true;
    }

    void PlayLoop(int gen, PlayRequest r)
    {
        int fsize = r.W * r.H * 3;
        bool firstPass = true;
        double start = r.Start, end = r.End;
        var buf = new byte[fsize];
        while (gen == playGen)
        {
            if (!firstPass && r.Trim() is { } tr) (start, end) = tr;      // the markers may have been moved meanwhile
            double pstart = firstPass ? r.T0 : start;
            bool waveOn = r.UseSound && WaveBegin(gen, r, pstart, end);
            var pieces = Timing.BuildPieces(pstart, end, r.Sections);
            int n = 0;
            for (int lvl = playLevel; lvl >= 0; lvl--)
            {
                var proc = Start(PreviewCommands.Play(r.Path, pstart, end - pstart, r.Crop, r.W, r.H, r.Fps, lvl, r.Trc, pieces, r.Effects));
                lock (gate) playProc = proc;
                var stdout = proc.StandardOutput.BaseStream;
                n = 0;
                var clock = Stopwatch.StartNew();
                double? tStart = null;
                try
                {
                    while (gen == playGen)
                    {
                        int got = 0, k;
                        while (got < fsize && (k = stdout.Read(buf, got, fsize - got)) > 0) got += k;
                        if (got < fsize) break;
                        double now = clock.Elapsed.TotalSeconds;
                        if (tStart is null)
                        {
                            tStart = now;
                            if (waveOn) wave?.Start();
                        }
                        double due = tStart.Value + n / r.Fps;               // real-time pacing
                        if (due > now) Thread.Sleep(TimeSpan.FromSeconds(due - now));
                        else if (n > 0 && now - due > 0.15) { n++; continue; }   // far behind: drop frames to catch up
                        double ts = Timing.OutToSrc(pieces, n / r.Fps);
                        if (!r.Once && r.Trim() is { } cur && ts > cur.End + 0.03) break;   // the end marker was moved back: loop now
                        Deliver(gen, buf, ts);
                        n++;
                    }
                }
                finally
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch (InvalidOperationException) { }
                    proc.WaitForExit();
                }
                if (gen != playGen) return;
                if (n > 0) break;
                if (lvl == playLevel && lvl > 0) playLevel = lvl - 1;      // that decoding mode is unavailable: step down
            }
            if (n == 0)
            {
                if (gen == playGen) ui.BeginInvoke(() => PlaybackFailed?.Invoke());
                return;
            }
            if (r.Once)
            {
                if (gen == playGen) ui.BeginInvoke(() => { if (gen == playGen) PlaybackEnded?.Invoke(); });
                return;
            }
            firstPass = false;
        }
    }

    // only the newest frame is kept while the screen is behind
    byte[]? pendingFrame;
    double pendingT;
    int pendingGen;
    bool drawPending;

    void Deliver(int gen, byte[] buf, double t)
    {
        lock (gate)
        {
            pendingFrame ??= new byte[buf.Length];
            if (pendingFrame.Length != buf.Length) pendingFrame = new byte[buf.Length];
            Buffer.BlockCopy(buf, 0, pendingFrame, 0, buf.Length);
            (pendingT, pendingGen) = (t, gen);
            if (drawPending) return;
            drawPending = true;
        }
        ui.BeginInvoke(DispatcherPriority.Render, DrawFrame);
    }

    void DrawFrame()
    {
        byte[] data;
        double t;
        lock (gate)
        {
            drawPending = false;
            if (pendingFrame is null || pendingGen != playGen || !Playing || Frame is null) return;
            data = pendingFrame;
            t = pendingT;
            var f = Frame;
            f.WritePixels(new Int32Rect(0, 0, f.PixelWidth, f.PixelHeight), data, f.PixelWidth * 3, 0);
        }
        FrameShown?.Invoke(t);
    }
}
