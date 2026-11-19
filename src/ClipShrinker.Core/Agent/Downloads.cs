using System.Net.Http;
using System.Text.RegularExpressions;

namespace ClipShrinker.Core.Agent;

/// <summary>2.8 AI editing module: files the agent asks for. Sound clips (small, downloaded while the tool call waits)
/// and the speech model (large, downloaded in the background while the agent carries on).</summary>
public static partial class Downloads
{
    static readonly HttpClient http = MakeClient();

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ClipShrinker/" + C.AppVersion);
        return c;
    }

    public const long SoundMaxBytes = 40L * 1024 * 1024;

    /// <summary>Downloads url into folder as a sound file (https only, at most 40 MB, the extension must be a sound
    /// type). name = the file name to use without extension (null = from the url). Returns the saved path. The caller
    /// checks that it really is sound (ffprobe) and deletes it when not.</summary>
    public static string Sound(string url, string folder, string? name)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new AgentToolError("Give a direct https:// link to the sound file.");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var resp = http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode) throw new AgentToolError($"The download failed: {(int)resp.StatusCode} {resp.ReasonPhrase}.");
        if (resp.Content.Headers.ContentLength is > SoundMaxBytes) throw new AgentToolError("That file is larger than 40 MB: pick a shorter sound.");
        string type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (type.StartsWith("text/") || type.Contains("html"))
            throw new AgentToolError("That link gives a web page, not a sound file. Find the direct download link (it usually ends in .mp3, .wav or .ogg).");

        string ext = Path.GetExtension(resp.RequestMessage?.RequestUri?.AbsolutePath ?? uri.AbsolutePath).ToLowerInvariant();
        string? fromHeader = resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName;
        if (fromHeader is { Length: > 0 } h && Sounds.IsSoundFile(h.Trim('"'))) ext = Path.GetExtension(h.Trim('"')).ToLowerInvariant();
        if (!Sounds.Exts.Contains(ext))
            ext = type switch
            {
                "audio/mpeg" or "audio/mp3" => ".mp3", "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
                "audio/ogg" or "application/ogg" => ".ogg", "audio/flac" or "audio/x-flac" => ".flac",
                "audio/mp4" or "audio/aac" or "audio/x-m4a" => ".m4a", "audio/opus" => ".opus",
                _ => throw new AgentToolError($"That is not a sound file Clip Shrinker can use ({string.Join(", ", Sounds.Exts)})."),
            };

        string stem = CleanName(name is { Length: > 0 } ? name
            : Path.GetFileNameWithoutExtension(fromHeader is { Length: > 0 } f ? f.Trim('"') : uri.AbsolutePath));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, stem + ext);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem} ({i}){ext}");

        string part = path + ".part";
        try
        {
            using (var src = resp.Content.ReadAsStream(cts.Token))
            using (var dst = File.Create(part))
            {
                var buf = new byte[81920];
                long total = 0;
                int n;
                while ((n = src.Read(buf)) > 0)
                {
                    total += n;
                    if (total > SoundMaxBytes) throw new AgentToolError("That file is larger than 40 MB: pick a shorter sound.");
                    dst.Write(buf, 0, n);
                }
            }
            File.Move(part, path);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
        return path;
    }

    [GeneratedRegex(@"[^\w\- .()]+")]
    private static partial Regex BadChars();

    /// <summary>A safe file name: letters, digits, space, - _ . ( ); at most 60 characters.</summary>
    public static string CleanName(string s)
    {
        string t = BadChars().Replace(Uri.UnescapeDataString(s ?? ""), " ").Trim(' ', '.');
        t = Regex.Replace(t, @"\s+", " ");
        if (t.Length > 60) t = t[..60].Trim();
        return t.Length > 0 ? t : "sound";
    }

    /// <summary>Notes where a downloaded sound came from (sources.txt next to the sounds), for licences / credits.</summary>
    public static void NoteSource(string folder, string file, string url, string? license)
    {
        try
        {
            File.AppendAllText(Path.Combine(folder, "sources.txt"),
                $"{DateTime.Now:yyyy-MM-dd}\t{Path.GetFileName(file)}\t{url}\t{(license is { Length: > 0 } l ? l : "licence not given")}\r\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------ large files in the background
    public sealed class Job
    {
        public required string Url { get; init; }
        public required string Path { get; init; }
        public long Done, Total;
        public string? Error;
        public bool Finished;
        public int Percent => Total > 0 ? (int)(100 * Done / Total) : 0;
    }

    static readonly Dictionary<string, Job> jobs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts (or returns the running) download of url to path. A finished job is forgotten so that a
    /// failed one can be tried again.</summary>
    public static Job Background(string url, string path)
    {
        lock (jobs)
        {
            if (jobs.TryGetValue(path, out var j) && !(j.Finished && j.Error is not null)) return j;
            var job = new Job { Url = url, Path = path };
            jobs[path] = job;
            _ = Task.Run(() => Run(job));
            return job;
        }
    }

    static async Task Run(Job job)
    {
        string part = job.Path + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.Path)!);
            using var resp = await http.GetAsync(job.Url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            job.Total = resp.Content.Headers.ContentLength ?? 0;
            await using (var src = await resp.Content.ReadAsStreamAsync())
            await using (var dst = File.Create(part))
            {
                var buf = new byte[1 << 20];
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    job.Done += n;
                }
            }
            if (job.Total > 0 && new FileInfo(part).Length != job.Total) throw new IOException("the download was cut off");
            File.Move(part, job.Path, true);
        }
        catch (Exception e) { job.Error = e.Message; }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { }
            job.Finished = true;
        }
    }
}
