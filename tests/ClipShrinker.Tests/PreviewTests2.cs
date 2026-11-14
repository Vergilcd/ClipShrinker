using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using ClipShrinker.Core;
using ClipShrinker.Core.Export;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Rife;

namespace ClipShrinker.Tests;

public class ZoomAreaTests
{
    [Fact]
    public void MoveStaysInsideThePicture()
    {
        Assert.Equal((0.35, 0.15), ZoomArea.Move(0.25, 0.25, 0.5, 0.1, -0.1));
        Assert.Equal((0.5, 0.0), ZoomArea.Move(0.25, 0.25, 0.5, 0.9, -0.9));
    }

    [Fact]
    public void ResizeKeepsTheOppositeCorner()
    {
        // se corner dragged out: nw corner (0.25, 0.25) stays, size follows the larger move, limited by the picture
        Assert.Equal((0.25, 0.25, 0.75), ZoomArea.Resize("se", 0.25, 0.25, 0.5, 1.2, 0.9));
        // nw corner dragged in: se corner (0.75, 0.75) stays, never below the minimum size
        var (x, y, w) = ZoomArea.Resize("nw", 0.25, 0.25, 0.5, 0.74, 0.74);
        Assert.Equal((0.65, 0.65, 0.1), (Math.Round(x, 9), Math.Round(y, 9), w));
        // ne corner: x stays at the left, y moves up
        var (x2, y2, w2) = ZoomArea.Resize("ne", 0.2, 0.4, 0.3, 0.6, 0.3);
        Assert.Equal((0.2, 0.3, 0.4), (Math.Round(x2, 9), Math.Round(y2, 9), Math.Round(w2, 9)));
    }

    [Fact]
    public void BracketTargetFollowsThePlayhead()
    {
        var clip = new TimelineClip("x.mp4", new MediaInfo(1920, 1080, 60, 20, 1, 0, 0, null, null, null));
        var ed = new TimelineEditor(clip) { Active = true, Playhead = 3 };
        var z1 = ed.AddZoom()!;                                  // at the playhead
        ed.Playhead = 10;
        var z2 = ed.AddZoom()!;
        ed.Playhead = 8;
        Assert.Null(ZoomArea.Target(ed));
        ed.Playhead = 11;
        Assert.Same(z2, ZoomArea.Target(ed));
        ed.Playhead = 3.5;
        ed.ZSel = z2;
        Assert.Same(z1, ZoomArea.Target(ed));                     // the selected one only when the playhead is in it
    }

    [Fact]
    public void PreviewSizes()
    {
        var uw = new MediaInfo(3440, 1440, 144, 10, 1, 0, 0, null, null, null);
        Assert.Equal((1600, 900), PreviewPlan.Dims(2500, 1200));
        Assert.Equal((640, 360), PreviewPlan.Dims(30, 30));
        Assert.Equal((2560, 1440), PreviewPlan.Crop(uw, 16.0 / 9));
        Assert.Equal((1600, 900), PreviewPlan.PlaySize(uw, 16.0 / 9, (1600, 900)));
        Assert.Equal((1000, 418), PreviewPlan.PlaySize(uw, null, (1000, 900)));
        Assert.Equal(30, PreviewPlan.PlayFps(uw));
        var s = AppState.FromSettings(null);
        s.ZoomLen = "9";
        Assert.Equal(2.0, PreviewPlan.Effects(s, []).ZoomLen);
        s.ZoomLen = "abc";
        Assert.Equal(0.4, PreviewPlan.Effects(s, []).ZoomLen);
    }
}

public class RifeDownloadTests
{
    /// <summary>A web server in memory: serves one file, with or without range requests.</summary>
    sealed class FakeServer(byte[] file, bool ranges) : HttpMessageHandler
    {
        public int Requests, BytesSent;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(Send(request, ct));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (ranges && request.Headers.Range?.Ranges.FirstOrDefault() is { } r)
            {
                long a = r.From ?? 0, b = Math.Min(r.To ?? file.Length - 1, file.Length - 1);
                var part = file.AsSpan((int)a, (int)(b - a + 1)).ToArray();
                BytesSent += part.Length;
                var resp = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(part), RequestMessage = request };
                resp.Content.Headers.ContentRange = new ContentRangeHeaderValue(a, b, file.Length);
                return resp;
            }
            BytesSent += file.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file), RequestMessage = request };
        }
    }

    /// <summary>A zip shaped like the RIFE release: the needed files plus a big model folder the app does not use.</summary>
    static byte[] FakeRelease()
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, int size)
            {
                using var s = z.CreateEntry(name, CompressionLevel.NoCompression).Open();
                var data = new byte[size];
                new Random(name.Length).NextBytes(data);
                s.Write(data);
            }
            Add("rife-ncnn-vulkan-20221029-windows/rife-ncnn-vulkan.exe", 50_000);
            Add("rife-ncnn-vulkan-20221029-windows/vcomp140.dll", 10_000);
            Add("rife-ncnn-vulkan-20221029-windows/LICENSE", 1_000);
            Add("rife-ncnn-vulkan-20221029-windows/rife-v4.6/flownet.param", 2_000);
            Add("rife-ncnn-vulkan-20221029-windows/rife-v4.6/flownet.bin", 100_000);
            Add("rife-ncnn-vulkan-20221029-windows/rife-UHD/flownet.bin", 6_000_000);    // not needed
        }
        return ms.ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstallsTheNeededFiles(bool ranges)
    {
        var zip = FakeRelease();
        var server = new FakeServer(zip, ranges);
        string dir = Path.Combine(Path.GetTempPath(), "cs_rife_" + Guid.NewGuid().ToString("N"));
        var steps = new List<string>();
        try
        {
            await RifeDownloader.Install(dir, (t, _) => steps.Add(t), default, "http://example.invalid/rife.zip", server);
            Assert.NotNull(RifeInstall.Find(dir));
            Assert.True(File.Exists(Path.Combine(dir, "rife-v4.6", "flownet.bin")));
            if (ranges)
            {
                Assert.False(Directory.Exists(Path.Combine(dir, "rife-UHD")));     // only the parts the app uses
                Assert.True(server.BytesSent < zip.Length / 2, $"{server.BytesSent} of {zip.Length} bytes were sent");
            }
            else Assert.Contains("Unpacking...", steps);                         // the whole-zip fallback
            Assert.Equal("Installing...", steps[^1]);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// <summary>The first-run ffmpeg download takes bin\ffmpeg.exe, ffprobe.exe and the DLLs, flat into the folder.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FfmpegDownloadTakesTheBinFolder(bool ranges)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, int size)
            {
                using var s = z.CreateEntry(name, CompressionLevel.NoCompression).Open();
                var data = new byte[size];
                new Random(name.Length).NextBytes(data);
                s.Write(data);
            }
            Add("ffmpeg-master-latest-win64-lgpl-shared/bin/ffmpeg.exe", 40_000);
            Add("ffmpeg-master-latest-win64-lgpl-shared/bin/ffprobe.exe", 40_000);
            Add("ffmpeg-master-latest-win64-lgpl-shared/bin/avcodec-62.dll", 80_000);
            Add("ffmpeg-master-latest-win64-lgpl-shared/bin/ffplay.exe", 40_000);           // not needed
            Add("ffmpeg-master-latest-win64-lgpl-shared/doc/ffmpeg.html", 3_000_000);        // not needed
        }
        var server = new FakeServer(ms.ToArray(), ranges);
        string dir = Path.Combine(Path.GetTempPath(), "cs_ffdl_" + Guid.NewGuid().ToString("N"));
        try
        {
            await ClipShrinker.Core.Media.FfmpegDownloader.Install(dir, (_, _) => { }, default, "http://example.invalid/ff.zip", server);
            Assert.Equal(["avcodec-62.dll", "ffmpeg.exe", "ffprobe.exe"], Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToArray());
            if (ranges) Assert.True(server.BytesSent < ms.Length / 2, $"{server.BytesSent} of {ms.Length} bytes were sent");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
