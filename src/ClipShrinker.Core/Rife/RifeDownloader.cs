using System.IO.Compression;
using System.Net.Http.Headers;
using ClipShrinker.Core.Export;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Rife;

/// <summary>Read-only, seekable view of a file on a web server (HTTP range requests), so ZipArchive can pull single
/// members out of a big zip without downloading the whole thing (reference RangeFile).</summary>
public sealed class HttpRangeStream : Stream
{
    const int Block = 1 << 20;
    readonly HttpClient http;
    readonly Action<long>? progress;
    readonly Uri url;
    byte[] buf = [];
    long bufAt, pos, got;

    public override long Length { get; }

    HttpRangeStream(HttpClient http, Uri finalUrl, long size, Action<long>? progress)
    {
        (this.http, url, Length, this.progress) = (http, finalUrl, size, progress);
    }

    /// <summary>Asks for byte 0 to learn the size; throws when the server does not support partial downloads.</summary>
    public static async Task<HttpRangeStream> OpenAsync(HttpClient http, string url, Action<long>? progress, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(0, 0);
        using var r = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (r.StatusCode != System.Net.HttpStatusCode.PartialContent || r.Content.Headers.ContentRange?.Length is not { } size)
            throw new IOException("the server does not support partial downloads");
        return new HttpRangeStream(http, r.RequestMessage?.RequestUri ?? new Uri(url), size, progress);   // final address after redirects
    }

    byte[] Fetch(long a, long n)
    {
        long b = Math.Min(Length, a + n) - 1;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(a, b);
        using var r = http.Send(req);
        r.EnsureSuccessStatusCode();
        using var s = r.Content.ReadAsStream();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        got += ms.Length;
        progress?.Invoke(got);
        return ms.ToArray();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (pos >= Length) return 0;
        count = (int)Math.Min(count, Length - pos);
        int done = 0;
        while (count > 0)
        {
            if (!(bufAt <= pos && pos < bufAt + buf.Length))
            {
                bufAt = pos;
                buf = Fetch(pos, Math.Max(count, Length - pos < 4 * Block ? 65536 : Block));
                if (buf.Length == 0) break;
            }
            int o = (int)(pos - bufAt);
            int k = Math.Min(count, buf.Length - o);
            Array.Copy(buf, o, buffer, offset + done, k);
            pos += k;
            done += k;
            count -= k;
        }
        return done;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        pos = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => pos + offset, _ => Length + offset };

    public override long Position { get => pos; set => pos = value; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Downloads rife-ncnn-vulkan into library\rife (reference download_rife / _rife_worker): first only the
/// files the app uses (about 20 MB instead of 430 MB) with range requests, else the whole zip.</summary>
public static class RifeDownloader
{
    public const string Url = "https://github.com/nihui/rife-ncnn-vulkan/releases/download/20221029/rife-ncnn-vulkan-20221029-windows.zip";

    /// <summary>The parts of the release the app uses (member names below the top folder start with these).</summary>
    public static readonly string[] Keep = ["rife-ncnn-vulkan.exe", "rife-ncnn-vulkan", "vcomp140.dll", "license", "rife-v4.6/"];

    static bool Wanted(string member)
    {
        string rest = member.ToLowerInvariant().Split('/', 2)[^1];
        return Keep.Any(rest.StartsWith) && !member.EndsWith('/');
    }

    /// <summary>Installs RIFE into rifeDir. status(text, percent) reports progress. Throws on failure with the
    /// reason; the caller shows the reference's "Download failed (...)" text.</summary>
    public static async Task Install(string rifeDir, Action<string, int?> status, CancellationToken ct = default,
                                     string url = Url, HttpMessageHandler? handler = null)
    {
        string tmp = Directory.CreateTempSubdirectory("clipshrinker_rifedl_").FullName;
        string xd = Path.Combine(tmp, "x");
        using var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShrinker");
        http.Timeout = TimeSpan.FromSeconds(60);
        try
        {
            bool doneSmall = false;
            try
            {
                long planned = 0;
                var last = DateTime.MinValue;
                status("Connecting...", 0);
                await using var rf = await HttpRangeStream.OpenAsync(http, url, gotBytes =>
                {
                    if (planned == 0 || (DateTime.Now - last).TotalSeconds < 0.2) return;
                    last = DateTime.Now;
                    int pct = (int)Math.Min(99, 100 * gotBytes / planned);
                    status($"Downloading RIFE: {F(Math.Min(gotBytes, planned) / 1048576.0, 1)} of {F(planned / 1048576.0, 1)} MB ({pct}%)", pct);
                }, ct);
                using var zf = new ZipArchive(rf, ZipArchiveMode.Read);
                var keep = zf.Entries.Where(e => Wanted(e.FullName)).ToList();
                if (!keep.Any(e => e.FullName.ToLowerInvariant().EndsWith("rife-ncnn-vulkan.exe") || e.FullName.ToLowerInvariant().EndsWith("flownet.bin")))
                    throw new IOException("unexpected zip layout");
                planned = keep.Sum(e => e.CompressedLength) + 120000;
                foreach (var e in keep)
                {
                    ct.ThrowIfCancellationRequested();
                    string dest = Path.GetFullPath(Path.Combine(xd, e.FullName));
                    if (!dest.StartsWith(Path.GetFullPath(xd) + Path.DirectorySeparatorChar)) continue;   // no paths outside
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, overwrite: true);
                }
                doneSmall = true;
            }
            catch (Exception e) when (e is IOException or HttpRequestException or InvalidDataException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (Directory.Exists(xd)) Directory.Delete(xd, true);
            }
            if (!doneSmall)
            {
                string z = Path.Combine(tmp, "rife.zip");
                using (var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    r.EnsureSuccessStatusCode();
                    long total = r.Content.Headers.ContentLength ?? 0, got = 0;
                    var last = DateTime.MinValue;
                    await using var src = await r.Content.ReadAsStreamAsync(ct);
                    await using var dst = File.Create(z);
                    var b = new byte[1 << 16];
                    int n;
                    while ((n = await src.ReadAsync(b, ct)) > 0)
                    {
                        await dst.WriteAsync(b.AsMemory(0, n), ct);
                        got += n;
                        if ((DateTime.Now - last).TotalSeconds > 0.2)
                        {
                            last = DateTime.Now;
                            int pct = total > 0 ? (int)Math.Min(99, 100 * got / total) : 0;
                            status($"Downloading RIFE (full package): {F(got / 1048576.0, 0)}" + (total > 0 ? $" of {F(total / 1048576.0, 0)}" : "") + $" MB ({pct}%)", pct);
                        }
                    }
                }
                status("Unpacking...", 99);
                ZipFile.ExtractToDirectory(z, xd, overwriteFiles: true);
            }
            status("Installing...", 100);
            string? exe = Directory.Exists(xd)
                ? Directory.EnumerateFiles(xd, "*", SearchOption.AllDirectories)
                           .FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant() is "rife-ncnn-vulkan.exe" or "rife-ncnn-vulkan")
                : null;
            if (exe is null) throw new IOException("the download does not contain rife-ncnn-vulkan");
            CopyTree(Path.GetDirectoryName(exe)!, rifeDir);
            if (RifeInstall.Find(rifeDir) is null) throw new IOException("the rife-v4 model files are missing in the download");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string d = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(d)!);
            File.Copy(f, d, overwrite: true);
        }
    }
}
