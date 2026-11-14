using System.IO.Compression;
using ClipShrinker.Core.Rife;

namespace ClipShrinker.Core.Media;

/// <summary>First-run download of ffmpeg / ffprobe when they are missing from the library folder (reference Setup.bat
/// step 1): the LGPL shared build from BtbN's GitHub releases. Only bin\ffmpeg.exe, bin\ffprobe.exe and the DLLs are
/// taken from the zip (range requests); a server without ranges gets the whole zip instead.</summary>
public static class FfmpegDownloader
{
    public const string Url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip";

    static bool Wanted(ZipArchiveEntry e)
    {
        string n = e.FullName.Replace('\\', '/').ToLowerInvariant();
        if (n.EndsWith('/') || !n.Contains("/bin/")) return false;
        string f = Path.GetFileName(n);
        return f is "ffmpeg.exe" or "ffprobe.exe" || f.EndsWith(".dll");
    }

    /// <summary>Puts ffmpeg.exe, ffprobe.exe and their DLLs into libDir. status(text, percent) reports progress.
    /// Throws (IOException / HttpRequestException ...) with the reason when it fails.</summary>
    public static async Task Install(string libDir, Action<string, int?> status, CancellationToken ct = default,
                                     string url = Url, HttpMessageHandler? handler = null)
    {
        string tmp = Directory.CreateTempSubdirectory("clipshrinker_ffmpegdl_").FullName;
        string xd = Path.Combine(tmp, "x");
        using var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShrinker");
        http.Timeout = TimeSpan.FromSeconds(60);
        try
        {
            bool done = false;
            try
            {
                long planned = 0;
                var last = DateTime.MinValue;
                status("Connecting...", 0);
                await using var rf = await HttpRangeStream.OpenAsync(http, url, got =>
                {
                    if (planned == 0 || (DateTime.Now - last).TotalSeconds < 0.2) return;
                    last = DateTime.Now;
                    int pct = (int)Math.Min(99, 100 * got / planned);
                    status($"Downloading ffmpeg: {Mb(Math.Min(got, planned))} of {Mb(planned)} MB ({pct}%)", pct);
                }, ct);
                using var zf = new ZipArchive(rf, ZipArchiveMode.Read);
                var keep = zf.Entries.Where(Wanted).ToList();
                planned = keep.Sum(e => e.CompressedLength) + 120000;
                Extract(keep, xd, ct);
                done = true;
            }
            catch (Exception e) when (e is IOException or HttpRequestException or InvalidDataException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (Directory.Exists(xd)) Directory.Delete(xd, true);
            }
            if (!done)                                             // no range requests: the whole zip
            {
                string z = Path.Combine(tmp, "ffmpeg.zip");
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
                            status($"Downloading ffmpeg: {Mb(got)}" + (total > 0 ? $" of {Mb(total)}" : "") + $" MB ({pct}%)", pct);
                        }
                    }
                }
                status("Unpacking...", 99);
                using var zf = ZipFile.OpenRead(z);
                Extract(zf.Entries.Where(Wanted).ToList(), xd, ct);
            }
            status("Installing...", 100);
            if (!File.Exists(Path.Combine(xd, "ffmpeg.exe")) || !File.Exists(Path.Combine(xd, "ffprobe.exe")))
                throw new IOException("the download does not contain ffmpeg.exe and ffprobe.exe");
            Directory.CreateDirectory(libDir);
            foreach (var f in Directory.EnumerateFiles(xd))
                File.Copy(f, Path.Combine(libDir, Path.GetFileName(f)), overwrite: true);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The files go flat into dir (the zip keeps them in a versioned top folder plus bin\).</summary>
    static void Extract(List<ZipArchiveEntry> entries, string dir, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            e.ExtractToFile(Path.Combine(dir, Path.GetFileName(e.FullName)), overwrite: true);
        }
    }

    static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}
