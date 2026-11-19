using ClipShrinker.Core.Analysis;
using ClipShrinker.Core.Media;

namespace ClipShrinker.Tests;

/// <summary>Dev (MCP research): the clip analysis the agent tools use.</summary>
public class AnalysisTests
{
    static readonly string Media = Path.Combine(Fixtures.FixtureDir, "..", "media");
    static readonly string Lib = Path.GetFullPath(Path.Combine(Fixtures.FixtureDir, "..", "..", "..", "Dev", "library"));
    static Tools? T => File.Exists(Path.Combine(Lib, "ffmpeg.exe")) && Directory.Exists(Media) ? Tools.Locate(Lib) : null;

    [Fact]
    public void PngIsWellFormed()
    {
        var png = Png.Encode(new byte[4 * 2 * 3], 4, 2);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png.Take(4));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(ClipShrinker.Core.Media.ThumbnailService.PngEnd, png.TakeLast(8));
    }

    [Fact]
    public void ClockAndEvenSizes()
    {
        Assert.Equal("1:12.5", ClipAnalysis.Clock(72.5));
        Assert.Equal("1:00:03.0", ClipAnalysis.Clock(3603));
        Assert.Equal((256, 144), ClipAnalysis.Fit(1920, 1088, 256));        // 145.07 -> even 144, so HDR clips tone-map
        Assert.Equal((256, 144), ClipAnalysis.Fit(1920, 1080, 257));
    }

    [Fact]
    public void LoudPeakIsFound()
    {
        var pcm = new short[8000 * 10];                                     // 10 s of quiet noise, a bang at 6 s
        var rnd = new Random(1);
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)rnd.Next(-300, 300);
        for (int i = 6 * 8000; i < 6 * 8000 + 1600; i++) pcm[i] = (short)rnd.Next(-20000, 20000);
        var peaks = ClipAnalysis.LoudPeaks(pcm, 8000, 100);
        var p = Assert.Single(peaks);
        Assert.InRange(p.Time, 106.0, 106.2);
        Assert.True(p.Strength > 20);
    }

    [Fact]
    public void PictureChangeIsFound()
    {
        var frames = new List<byte[]>();
        var rnd = new Random(2);
        for (int i = 0; i < 40; i++)
        {
            var f = new byte[32 * 18];
            for (int j = 0; j < f.Length; j++) f[j] = (byte)((i < 25 ? 60 : 200) + rnd.Next(0, 4));   // a cut at frame 25
            frames.Add(f);
        }
        var c = Assert.Single(ClipAnalysis.PictureChanges(frames, 0.5, 10));
        Assert.Equal(22.5, c.Time, 3);
    }

    [Fact]
    public void BothKindsGetPlaces()
    {
        var loud = Enumerable.Range(0, 20).Select(i => new Moment(i * 5, "loud", 20 + i));
        var pic = Enumerable.Range(0, 3).Select(i => new Moment(i * 7 + 1, "picture change", 5));
        var picked = ClipAnalysis.Pick(loud.Concat(pic), 12);
        Assert.Equal(12, picked.Count);
        Assert.Equal(3, picked.Count(m => m.Kind == "picture change"));      // not pushed out by the bigger dB numbers
        Assert.True(picked.Zip(picked.Skip(1)).All(p => p.First.Time <= p.Second.Time));
    }

    [Fact]
    public void StatsOfGreyAndRed()
    {
        var grey = Enumerable.Repeat((byte)128, 4 * 3).ToArray();
        var red = new byte[] { 255, 0, 0, 255, 0, 0 };
        var s = ClipAnalysis.Stats([grey, red]);
        Assert.Equal(6, s.Frames * 3);
        Assert.InRange(s.MeanSat, 0.3, 0.4);                                 // 2 of 6 pixels fully saturated
    }

    [Fact]
    public void RealClip()
    {
        if (T is not { } t) return;
        string a = Path.Combine(Media, "a_1080p60.mp4");
        var (png, times) = ClipAnalysis.ContactSheet(t.Ffmpeg!, a, 1920, 1080, null, 0, 10, 2, 4, 128);
        Assert.Equal([0.0, 2, 4, 6, 8], times);
        Assert.Equal(ClipShrinker.Core.Media.ThumbnailService.PngEnd, png.TakeLast(8));
        Assert.True(ClipAnalysis.MotionFrames(t.Ffmpeg!, a, 0, 10, false).Count is >= 19 and <= 20);
        var hdr = ClipAnalysis.SampleFrames(t.Ffmpeg!, Path.Combine(Media, "d_hdr10.mp4"), [0.5], 254, 142, "smpte2084");
        Assert.NotNull(hdr[0]);                                              // tone-mapped at an even size
        Assert.True(ClipAnalysis.Picture(t.Ffmpeg!, a, null, 0, 10).Frames > 0);
    }
}
