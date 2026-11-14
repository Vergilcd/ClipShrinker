using System.IO;
using System.Runtime.InteropServices;

namespace ClipShrinker.App.Media;

/// <summary>
/// Gapless audio through the Windows waveOut API (reference WaveOut / _WinMM): ffmpeg streams 44.1 kHz stereo
/// 16-bit PCM, which is queued in small buffers. It opens paused and pre-filled, and Start() releases it when the
/// first video frame appears, so picture and sound begin together.
/// </summary>
public sealed class WaveOut : IDisposable
{
    const int Blk = 17640;                       // 100 ms of 44.1 kHz stereo 16-bit
    const int NBuf = 10;
    const uint WaveMapper = 0xFFFFFFFF;
    const int WhdrDone = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormatEx
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags, dwLoops;
        public IntPtr lpNext, reserved;
    }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WaveFormatEx fmt, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutPause(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutRestart(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr h, uint v);

    static readonly int HdrSize = Marshal.SizeOf<WaveHdr>();
    static readonly int FlagsOffset = (int)Marshal.OffsetOf<WaveHdr>(nameof(WaveHdr.dwFlags));
    static readonly int LengthOffset = (int)Marshal.OffsetOf<WaveHdr>(nameof(WaveHdr.dwBufferLength));

    readonly IntPtr h;
    readonly IntPtr[] bufs = new IntPtr[NBuf], hdrs = new IntPtr[NBuf];
    readonly object gate = new();
    volatile bool stopFlag, closed, started;
    volatile bool muted;

    /// <summary>Preview volume 0..1 (the speaker slider); the export is not affected.</summary>
    public static double Volume { get; set; } = 1.0;

    public WaveOut()
    {
        var f = new WaveFormatEx { wFormatTag = 1, nChannels = 2, nSamplesPerSec = 44100, nAvgBytesPerSec = 44100 * 4, nBlockAlign = 4, wBitsPerSample = 16 };
        if (waveOutOpen(out h, WaveMapper, ref f, IntPtr.Zero, IntPtr.Zero, 0) != 0) throw new IOException("waveOutOpen failed");
        for (int i = 0; i < NBuf; i++)
        {
            bufs[i] = Marshal.AllocHGlobal(Blk);
            hdrs[i] = Marshal.AllocHGlobal(HdrSize);
            Marshal.StructureToPtr(new WaveHdr { lpData = bufs[i], dwBufferLength = Blk }, hdrs[i], false);
            if (waveOutPrepareHeader(h, hdrs[i], HdrSize) != 0) throw new IOException("waveOutPrepareHeader failed");
            Marshal.WriteInt32(hdrs[i], FlagsOffset, Marshal.ReadInt32(hdrs[i], FlagsOffset) | WhdrDone);   // slot is free
        }
        waveOutPause(h);
        ApplyVolume();
    }

    /// <summary>Mute and the preview volume are set on the output stream, so they act at once. (1.0.8 changed the
    /// samples before queueing them, which only became audible after the second of queued sound had played.)</summary>
    public void ApplyVolume()
    {
        uint v = muted ? 0u : (uint)Math.Round(Math.Max(0, Math.Min(1, Volume)) * 0xFFFF);
        lock (gate) if (!closed) waveOutSetVolume(h, v | (v << 16));      // left and right channel
    }

    bool Free(int i) => (Marshal.ReadInt32(hdrs[i], FlagsOffset) & WhdrDone) != 0;

    /// <summary>Reads PCM from the stream on a background thread and queues it.</summary>
    public void Feed(Stream pcm)
    {
        fed = true;
        new Thread(() => Run(pcm)) { IsBackground = true, Name = "preview audio" }.Start();
    }

    volatile bool fed;

    void Run(Stream pcm)
    {
        var data = new byte[Blk];
        try
        {
            int i = 0;
            while (!stopFlag)
            {
                int n = 0, r;
                while (n < Blk && (r = pcm.Read(data, n, Blk - n)) > 0) n += r;
                if (n == 0) break;
                while (!stopFlag && !Free(i)) Thread.Sleep(4);
                if (stopFlag) break;
                lock (gate)
                {
                    if (closed) break;
                    Marshal.Copy(data, 0, bufs[i], n);
                    Marshal.WriteInt32(hdrs[i], LengthOffset, n);
                    Marshal.WriteInt32(hdrs[i], FlagsOffset, Marshal.ReadInt32(hdrs[i], FlagsOffset) & ~WhdrDone);
                    waveOutWrite(h, hdrs[i], HdrSize);
                }
                i = (i + 1) % NBuf;
                if (n < Blk) break;
            }
            while (!stopFlag && !Enumerable.Range(0, NBuf).All(Free)) Thread.Sleep(20);   // let the tail play out
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally { Close(); }
    }

    public void SetMuted(bool flag)
    {
        muted = flag;
        ApplyVolume();
    }

    /// <summary>Releases the paused output (at the first video frame).</summary>
    public void Start()
    {
        if (started || stopFlag) return;
        started = true;
        lock (gate) if (!closed) waveOutRestart(h);
    }

    public void Stop()
    {
        stopFlag = true;
        lock (gate) if (!closed) waveOutReset(h);
        if (!fed) Close();                                     // no feeding thread that would close it
    }

    void Close()
    {
        lock (gate)
        {
            if (closed) return;
            closed = true;
            waveOutReset(h);
            for (int i = 0; i < NBuf; i++)
            {
                waveOutUnprepareHeader(h, hdrs[i], HdrSize);
                Marshal.FreeHGlobal(hdrs[i]);
                Marshal.FreeHGlobal(bufs[i]);
            }
            waveOutClose(h);
        }
    }

    public void Dispose() => Stop();
}
