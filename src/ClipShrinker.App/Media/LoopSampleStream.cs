using System.IO;

namespace ClipShrinker.App.Media;

/// <summary>
/// 2.9.1: the effect sample behind a held volume slider. Two renders of the same sample (44.1 kHz stereo 16-bit WAV):
/// one at 0 % and one at 100 % of the effect's volume. Read() loops them as quiet + volume x (loud - quiet), which is
/// what the volume setting does (the section's sound for speed / slow, the whoosh for zoom), with the volume read
/// again for every block. It stays only a little ahead of the clock, so the sound follows the slider within about
/// a quarter second instead of the second the player could queue.
/// </summary>
public sealed class LoopSampleStream : Stream
{
    readonly short[] quiet, loud;
    readonly Func<double> volume;
    readonly System.Diagnostics.Stopwatch clock = new();
    long produced;                                              // stereo frames handed out so far
    int pos;                                                    // in samples (2 per frame)
    const double Ahead = 0.25;                                  // seconds of sound queued at most

    public LoopSampleStream(string quietWav, string loudWav, Func<double> volume)
    {
        quiet = ReadPcm(quietWav);
        loud = ReadPcm(loudWav);
        if (loud.Length != quiet.Length) Array.Resize(ref loud, quiet.Length);
        this.volume = volume;
    }

    /// <summary>The 16-bit samples of a WAV file's data chunk.</summary>
    static short[] ReadPcm(string path)
    {
        var b = File.ReadAllBytes(path);
        int p = 12;
        while (p + 8 <= b.Length)
        {
            int len = BitConverter.ToInt32(b, p + 4);
            if (b[p] == 'd' && b[p + 1] == 'a' && b[p + 2] == 't' && b[p + 3] == 'a')
            {
                len = Math.Min(len, b.Length - p - 8) & ~3;
                var s = new short[len / 2];
                Buffer.BlockCopy(b, p + 8, s, 0, len);
                return s;
            }
            p += 8 + len + (len & 1);
        }
        throw new InvalidDataException("no data chunk in " + path);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (quiet.Length == 0) return 0;
        if (!clock.IsRunning) clock.Start();
        while (produced / 44100.0 - clock.Elapsed.TotalSeconds > Ahead) Thread.Sleep(10);   // keep close to the clock
        double v = Math.Max(0, Math.Min(2, volume()));
        int frames = count / 4;
        for (int f = 0; f < frames; f++)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                double q = quiet[pos + ch], l = loud[pos + ch];
                int s = (int)Math.Round(q + v * (l - q));
                s = Math.Max(short.MinValue, Math.Min(short.MaxValue, s));
                buffer[offset + f * 4 + ch * 2] = (byte)s;
                buffer[offset + f * 4 + ch * 2 + 1] = (byte)(s >> 8);
            }
            pos += 2;
            if (pos >= quiet.Length) pos = 0;                    // loop
        }
        produced += frames;
        return frames * 4;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
