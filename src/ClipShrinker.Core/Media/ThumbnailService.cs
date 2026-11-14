using System.Diagnostics;
using ClipShrinker.Core.Ffmpeg;

namespace ClipShrinker.Core.Media;

/// <summary>Who asked for thumbnails: the active clip is served first (all workers help with it), then the others
/// in list order.</summary>
public interface IThumbOwner
{
    bool ThumbActive { get; }
    int ThumbIndex { get; }
}

/// <summary>
/// Worker threads that make the timeline thumbnails in small chunks (reference ThumbPool and grab_frames).
/// A newer request of a clip replaces its older chunks.
/// </summary>
public sealed class ThumbnailService
{
    public static readonly byte[] PngEnd = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];   // "IEND" + CRC

    readonly object gate = new();
    readonly List<(IThumbOwner Owner, Action Job)> jobs = [];
    int activeRunning;
    public int Workers { get; }

    public ThumbnailService(int? workers = null)
    {
        Workers = workers ?? Math.Max(2, Math.Min(8, Environment.ProcessorCount / 2));
        for (int i = 0; i < Workers; i++)
            new Thread(Run) { IsBackground = true, Name = $"thumbs {i}", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public void Submit(IThumbOwner owner, IEnumerable<Action> fns)
    {
        lock (gate)
        {
            jobs.RemoveAll(j => ReferenceEquals(j.Owner, owner));
            jobs.AddRange(fns.Select(f => (owner, f)));
            Monitor.PulseAll(gate);
        }
    }

    public void Drop(IThumbOwner owner)
    {
        lock (gate) jobs.RemoveAll(j => ReferenceEquals(j.Owner, owner));
    }

    void Run()
    {
        while (true)
        {
            (IThumbOwner Owner, Action Job) job;
            bool isAct;
            lock (gate)
            {
                while (true)
                {
                    if (jobs.Count > 0)
                    {
                        job = jobs.MinBy(j => (j.Owner.ThumbActive ? 0 : 1, j.Owner.ThumbIndex));
                        isAct = job.Owner.ThumbActive;
                        if (isAct || activeRunning == 0) break;
                    }
                    Monitor.Wait(gate);
                }
                jobs.Remove(job);
                if (isAct) activeRunning++;
            }
            try { job.Job(); }
            catch (Exception e) { Debug.WriteLine("thumbnail job failed: " + e); }
            finally
            {
                lock (gate)
                {
                    if (isAct) activeRunning--;
                    Monitor.PulseAll(gate);
                }
            }
        }
    }

    /// <summary>Thumbnails for many times with one ffmpeg process per batch; onFrame(index, png) as soon as a frame is
    /// ready. Frames a batch did not deliver are made one by one. Returns the number of frames delivered.</summary>
    public static int GrabFrames(string path, IReadOnlyList<double> times, int w, int h, string? trc, Func<bool> stop,
                                 Action<int, byte[]> onFrame)
    {
        int done = 0;
        for (int b0 = 0; b0 < times.Count; b0 += C.ThumbBatch)
        {
            if (stop()) return done;
            var batch = times.Skip(b0).Take(C.ThumbBatch).ToList();
            int got = 0;
            try
            {
                got = RunPngStream(PreviewCommands.GrabFramesBatch(path, batch, w, h, trc), stop, png =>
                {
                    onFrame(b0 + got, png);
                    got++;
                }, batch.Count);
            }
            catch (System.ComponentModel.Win32Exception) { }
            done += got;
            for (int j = got; j < batch.Count; j++)                // fallback: what the batch run did not deliver
            {
                if (stop()) return done;
                if (GrabFrame(PreviewCommands.GrabFrame(path, batch[j], w, h, null, trc)) is { } png)
                {
                    onFrame(b0 + j, png);
                    done++;
                }
            }
        }
        return done;
    }

    /// <summary>One frame as PNG bytes, or null.</summary>
    public static byte[]? GrabFrame(IReadOnlyList<string> cmd)
    {
        try
        {
            using var p = Start(cmd);
            using var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            p.WaitForExit();
            return p.ExitCode == 0 && ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    static Process Start(IReadOnlyList<string> cmd)
    {
        var psi = new ProcessStartInfo(cmd[0])
        {
            RedirectStandardOutput = true, RedirectStandardError = false, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
        var p = Process.Start(psi)!;
        p.StandardInput.Close();
        return p;
    }

    /// <summary>Reads a stream of PNG images from ffmpeg's stdout, split at each IEND chunk.</summary>
    static int RunPngStream(IReadOnlyList<string> cmd, Func<bool> stop, Action<byte[]> onPng, int max)
    {
        using var p = Start(cmd);
        var stdout = p.StandardOutput.BaseStream;
        var buf = new List<byte>(1 << 16);
        var chunk = new byte[65536];
        int count = 0;
        try
        {
            int n;
            while ((n = stdout.Read(chunk, 0, chunk.Length)) > 0)
            {
                buf.AddRange(chunk.AsSpan(0, n));
                while (true)
                {
                    int k = IndexOf(buf, PngEnd);
                    if (k < 0) break;
                    var img = buf.GetRange(0, k + PngEnd.Length).ToArray();
                    buf.RemoveRange(0, k + PngEnd.Length);
                    if (count < max) onPng(img);
                    count++;
                }
                if (stop()) break;
            }
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
            p.WaitForExit();
        }
        return Math.Min(count, max);
    }

    static int IndexOf(List<byte> hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Count; i++)
        {
            int j = 0;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
