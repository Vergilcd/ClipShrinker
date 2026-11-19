using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipShrinker.Core.Modules;

/// <summary>2.8: one downloadable part of the AI editing module (ai-editing.json). Kind "zip" is unpacked into Dest,
/// kind "file" is put there as it is. Url may be relative to the manifest's folder; Fallbacks are other addresses
/// of the same bytes (the speech model on Hugging Face).</summary>
public sealed record AiPart(string Id, string Name, string Version, string File, string Url, long Size, string Sha256,
                            string Kind, string Dest, bool Optional, IReadOnlyList<string> Fallbacks);

/// <summary>2.8: ai-editing.json, the list of the AI editing module's parts on the GitHub release "ai-editing".</summary>
public sealed record AiManifest(string ModuleVersion, string MinApp, IReadOnlyList<AiPart> Parts)
{
    public static AiManifest Parse(string json)
    {
        var o = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("the AI editing manifest is not a JSON object");
        static string S(JsonNode? n, string key, string def = "") =>
            n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : def;
        var parts = new List<AiPart>();
        foreach (var p in o["parts"] as JsonArray ?? [])
        {
            if (p is not JsonObject po) continue;
            long size = po["size"] is JsonValue sv && sv.TryGetValue<long>(out var l) ? l : -1;
            string id = S(po, "id"), file = S(po, "file"), sha = S(po, "sha256").ToLowerInvariant(), kind = S(po, "kind", "file"), dest = S(po, "dest");
            if (id.Length == 0 || file.Length == 0 || size < 0 || sha.Length != 64 || kind is not ("zip" or "file")
                || dest is not (AiModule.HelperFolder or AiModule.FfmpegFolder or AiModule.ModelsFolder)
                || file.Contains('/') || file.Contains('\\') || file.Contains(".."))
                throw new InvalidDataException($"the AI editing manifest has a bad part ({(id.Length > 0 ? id : "no id")})");
            var fb = (po["fallbacks"] as JsonArray ?? []).Select(x => x is JsonValue xv && xv.TryGetValue<string>(out var u) ? u : null)
                                                         .Where(u => u is { Length: > 0 }).Cast<string>().ToList();
            bool opt = po["optional"] is JsonValue ov && ov.TryGetValue<bool>(out var ob) && ob;
            parts.Add(new AiPart(id, S(po, "name", id), S(po, "version", "?"), file, S(po, "url", file), size, sha, kind, dest, opt, fb));
        }
        if (parts.Count == 0) throw new InvalidDataException("the AI editing manifest lists no parts");
        if (!parts.Any(p => p.Dest == AiModule.HelperFolder)) throw new InvalidDataException("the AI editing manifest has no MCP helper");
        return new AiManifest(S(o, "version", "?"), S(o, "min_app", "0"), parts);
    }

    public string ToJson() => new JsonObject
    {
        ["version"] = ModuleVersion,
        ["min_app"] = MinApp,
        ["parts"] = new JsonArray(Parts.Select(p => (JsonNode)new JsonObject
        {
            ["id"] = p.Id, ["name"] = p.Name, ["version"] = p.Version, ["file"] = p.File, ["url"] = p.Url, ["size"] = p.Size,
            ["sha256"] = p.Sha256, ["kind"] = p.Kind, ["dest"] = p.Dest, ["optional"] = p.Optional,
            ["fallbacks"] = new JsonArray(p.Fallbacks.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
        }).ToArray()),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>2.8: the AI editing module is not in the base package. Its parts (the MCP helper, the ffmpeg build with
/// whisper and the speech model) are downloaded from the GitHub release "ai-editing" into modules\ai-agent\:
///   helper\clipshrinker-mcp.exe, ffmpeg\ffmpeg.exe (used only for speech), models\*.bin, installed.json.
/// library\ffmpeg.exe stays the base build: previews and exports never use the whisper build.
/// CLIPSHRINKER_AI_SOURCE = another folder or address that holds ai-editing.json (tests, local assets).</summary>
public static class AiModule
{
    public const string DefaultSource = "https://github.com/Vergilcd/ClipShrinker/releases/download/ai-editing/";
    public const string ManifestName = "ai-editing.json";
    public const string HelperFolder = "helper", FfmpegFolder = "ffmpeg", ModelsFolder = "models";
    public const string HelperExeName = "clipshrinker-mcp.exe";
    public const string InstalledName = "installed.json";
    public const string ServerName = "clipshrinker";

    public static string Source =>
        Environment.GetEnvironmentVariable("CLIPSHRINKER_AI_SOURCE") is { Length: > 0 } s ? s : DefaultSource;

    public static string Dir(string appDir) => Path.Combine(appDir, "modules", ModuleIds.AiAgent);
    public static string HelperExe(string appDir) => Path.Combine(Dir(appDir), HelperFolder, HelperExeName);
    public static string FfmpegExe(string appDir) => Path.Combine(Dir(appDir), FfmpegFolder, "ffmpeg.exe");
    public static string ModelsDir(string appDir) => Path.Combine(Dir(appDir), ModelsFolder);
    static string InstalledFile(string appDir) => Path.Combine(Dir(appDir), InstalledName);
    static string DownloadDir(string appDir) => Path.Combine(Dir(appDir), ".download");

    /// <summary>The helper is there and installed.json says what was installed.</summary>
    public static bool IsInstalled(string appDir) => File.Exists(HelperExe(appDir)) && System.IO.File.Exists(InstalledFile(appDir));

    /// <summary>The ffmpeg with the whisper filter that the module installed (null when not installed).</summary>
    public static string? SpeechFfmpeg(string appDir) => File.Exists(FfmpegExe(appDir)) ? FfmpegExe(appDir) : null;

    // ------------------------------------------------------------------ installed.json
    public sealed record InstalledPart(string Id, string Version, string Sha256, string File);

    public static Dictionary<string, InstalledPart> ReadInstalled(string appDir)
    {
        var res = new Dictionary<string, InstalledPart>();
        try
        {
            if (JsonNode.Parse(System.IO.File.ReadAllText(InstalledFile(appDir)))?["parts"] is JsonObject parts)
                foreach (var (id, n) in parts)
                    if (n is JsonObject o)
                        res[id] = new InstalledPart(id, (string?)o["version"] ?? "?", (string?)o["sha256"] ?? "", (string?)o["file"] ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
        return res;
    }

    public static string InstalledVersion(string appDir)
    {
        try { return (string?)JsonNode.Parse(System.IO.File.ReadAllText(InstalledFile(appDir)))?["version"] ?? "?"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return "?"; }
    }

    static void WriteInstalled(string appDir, string version, Dictionary<string, InstalledPart> parts)
    {
        var o = new JsonObject
        {
            ["version"] = version,
            ["installed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["parts"] = new JsonObject(parts.OrderBy(kv => kv.Key).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)new JsonObject
            {
                ["version"] = kv.Value.Version, ["sha256"] = kv.Value.Sha256, ["file"] = kv.Value.File,
            }))),
        };
        string f = InstalledFile(appDir);
        System.IO.File.WriteAllText(f + ".tmp", o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        System.IO.File.Move(f + ".tmp", f, overwrite: true);
    }

    /// <summary>Parts of the manifest that are not installed (or not as this version). Optional parts that were never
    /// installed are only listed when withOptional.</summary>
    public static List<AiPart> Missing(string appDir, AiManifest m, bool withOptional)
    {
        var have = ReadInstalled(appDir);
        return m.Parts.Where(p => have.TryGetValue(p.Id, out var h) ? h.Sha256 != p.Sha256 || !PartPresent(appDir, p)
                                                                    : !p.Optional || withOptional).ToList();
    }

    /// <summary>The updates for what is installed (new versions of installed parts; parts the manifest added that are
    /// not optional).</summary>
    public static List<AiPart> Updates(string appDir, AiManifest m)
    {
        var have = ReadInstalled(appDir);
        return m.Parts.Where(p => have.TryGetValue(p.Id, out var h) ? h.Sha256 != p.Sha256 : !p.Optional).ToList();
    }

    static bool PartPresent(string appDir, AiPart p) => p.Kind == "file"
        ? File.Exists(Path.Combine(Dir(appDir), p.Dest, p.File))
        : Directory.Exists(Path.Combine(Dir(appDir), p.Dest)) && Directory.EnumerateFiles(Path.Combine(Dir(appDir), p.Dest)).Any();

    // ------------------------------------------------------------------ download + install
    static HttpClient NewClient(HttpMessageHandler? handler)
    {
        var c = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        c.Timeout = Timeout.InfiniteTimeSpan;
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"{C.AppName.Replace(" ", "")}/{C.AppVersion}");
        return c;
    }

    static bool IsWeb(string s) => s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <summary>A local folder (or file:// address) as a path; null for web addresses.</summary>
    static string? LocalPath(string s) =>
        IsWeb(s) ? null : s.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(s).LocalPath : s;

    static string Combine(string source, string rel)
    {
        if (IsWeb(rel) || rel.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(rel)) return rel;
        if (LocalPath(source) is { } dir) return Path.Combine(dir, rel);
        return source.TrimEnd('/') + "/" + Uri.EscapeDataString(rel);
    }

    /// <summary>Reads ai-editing.json from source (a release download address or a folder).</summary>
    public static async Task<AiManifest> FetchManifest(string? source = null, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        source ??= Source;
        string where = Combine(source, ManifestName);
        if (LocalPath(where) is { } local) return AiManifest.Parse(await System.IO.File.ReadAllTextAsync(local, ct));
        using var http = NewClient(handler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var r = await http.GetAsync(where, cts.Token);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new IOException("the AI editing download is not published yet (ai-editing.json was not found)");
        r.EnsureSuccessStatusCode();
        return AiManifest.Parse(await r.Content.ReadAsStringAsync(cts.Token));
    }

    /// <summary>Downloads and installs parts (in the manifest's order, the helper last so that a broken install never
    /// looks installed). status(text, percent of all bytes). The previous files of a part are replaced only after its
    /// download is complete and its size and SHA-256 match. A running helper is renamed to *.old first.</summary>
    public static async Task Install(string appDir, AiManifest m, IReadOnlyList<AiPart> parts, Action<string, int> status,
                                     CancellationToken ct = default, string? source = null, HttpMessageHandler? handler = null)
    {
        if (ModuleManifest.CompareVersions(C.AppVersion.Split('-')[0], m.MinApp) < 0)
            throw new IOException($"this AI editing version needs Clip Shrinker {m.MinApp} or newer");
        source ??= Source;
        string dl = DownloadDir(appDir);
        Directory.CreateDirectory(dl);
        CleanOld(appDir);
        var order = parts.OrderBy(p => p.Dest == HelperFolder ? 1 : 0).ToList();
        long all = Math.Max(1, order.Sum(p => p.Size)), before = 0;
        using var http = NewClient(handler);
        var have = ReadInstalled(appDir);
        foreach (var p in order)
        {
            ct.ThrowIfCancellationRequested();
            string file = Path.Combine(dl, p.File);
            long b0 = before;
            await Fetch(http, source, p, file, got =>
                status($"Downloading {p.Name}: {got / 1048576.0:0.0} of {p.Size / 1048576.0:0.0} MB",
                       (int)Math.Min(99, 100 * (b0 + got) / all)), ct);
            status($"Installing {p.Name}...", (int)Math.Min(99, 100 * (before + p.Size) / all));
            Place(appDir, p, file);
            have[p.Id] = new InstalledPart(p.Id, p.Version, p.Sha256, p.File);
            // an update records each part as it lands; a first install only at the end, so that a broken one never
            // looks installed
            if (System.IO.File.Exists(InstalledFile(appDir))) WriteInstalled(appDir, m.ModuleVersion, have);
            before += p.Size;
        }
        if (File.Exists(HelperExe(appDir))) WriteInstalled(appDir, m.ModuleVersion, have);
        try { Directory.Delete(dl, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        status("Installed", 100);
    }

    /// <summary>Gets one part into file (.part while it comes; a .part from an interrupted download is continued) and
    /// checks its size and SHA-256. Tries the manifest address, then the fallbacks.</summary>
    static async Task Fetch(HttpClient http, string source, AiPart p, string file, Action<long> progress, CancellationToken ct)
    {
        if (System.IO.File.Exists(file) && new FileInfo(file).Length == p.Size && await Sha256(file, ct) == p.Sha256) { progress(p.Size); return; }
        System.IO.File.Delete(file);
        var errors = new List<string>();
        foreach (var url in new[] { Combine(source, p.Url) }.Concat(p.Fallbacks))
        {
            string part = file + ".part";
            try
            {
                if (LocalPath(url) is { } local)
                {
                    await using (var src = System.IO.File.OpenRead(local))
                    await using (var dst = System.IO.File.Create(part))
                        await Copy(src, dst, 0, progress, ct);
                }
                else
                {
                    long have = System.IO.File.Exists(part) ? new FileInfo(part).Length : 0;
                    if (have >= p.Size) { System.IO.File.Delete(part); have = 0; }
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
                    using var r = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    r.EnsureSuccessStatusCode();
                    bool resumed = have > 0 && r.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    await using var src = await r.Content.ReadAsStreamAsync(ct);
                    await using var dst = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write);
                    await Copy(src, dst, resumed ? have : 0, progress, ct);
                }
                long len = new FileInfo(part).Length;
                if (len != p.Size) throw new IOException($"{p.File} has {len} bytes, expected {p.Size}");
                if (await Sha256(part, ct) != p.Sha256) { System.IO.File.Delete(part); throw new IOException($"{p.File} is damaged (SHA-256 does not match)"); }
                System.IO.File.Move(part, file, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException && !ct.IsCancellationRequested)
            {
                errors.Add(e.Message);
                if (new FileInfo(part) is { Exists: true } fi && fi.Length > p.Size) System.IO.File.Delete(part);
            }
        }
        throw new IOException($"{p.Name} could not be downloaded ({string.Join("; ", errors.Distinct())})");
    }

    static async Task Copy(Stream src, Stream dst, long start, Action<long> progress, CancellationToken ct)
    {
        var buf = new byte[1 << 17];
        long got = start;
        var last = DateTime.MinValue;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            got += n;
            if ((DateTime.Now - last).TotalMilliseconds > 200) { last = DateTime.Now; progress(got); }
        }
        progress(got);
    }

    public static async Task<string> Sha256(string file, CancellationToken ct = default)
    {
        await using var s = System.IO.File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct));
    }

    /// <summary>Puts a checked download into modules\ai-agent\&lt;dest&gt;. A zip replaces the folder's files (a file
    /// that is running, like the helper while Claude Code has it open, is renamed to *.old and deleted later).</summary>
    static void Place(string appDir, AiPart p, string file)
    {
        string dest = Path.Combine(Dir(appDir), p.Dest);
        Directory.CreateDirectory(dest);
        if (p.Kind == "file")
        {
            string to = Path.Combine(dest, p.File);
            MoveAside(to);
            System.IO.File.Move(file, to);
            return;
        }
        string tmp = Path.Combine(Path.GetDirectoryName(file)!, p.Id + "_x");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        using (var z = ZipFile.OpenRead(file))
        {
            string root = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
            foreach (var e in z.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                string to = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException($"{p.File} has a file outside its folder");
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                e.ExtractToFile(to);
            }
        }
        if (p.Dest == HelperFolder && !System.IO.File.Exists(Path.Combine(tmp, HelperExeName)))
            throw new IOException($"{p.File} does not contain {HelperExeName}");
        if (p.Dest == FfmpegFolder && !System.IO.File.Exists(Path.Combine(tmp, "ffmpeg.exe")))
            throw new IOException($"{p.File} does not contain ffmpeg.exe");
        // the folder's old files go (the zip is the whole part), new ones come in
        foreach (var old in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).ToList())
            if (!old.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) MoveAside(old);
        foreach (var f in Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dest, Path.GetRelativePath(tmp, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            System.IO.File.Move(f, to);
        }
        Directory.Delete(tmp, true);
    }

    /// <summary>Deletes path, or renames it to path.N.old when it is in use (Windows lets a running exe be renamed).</summary>
    static void MoveAside(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        try { System.IO.File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.IO.File.Move(path, $"{path}.{DateTime.Now:yyyyMMddHHmmss}.old");
        }
    }

    /// <summary>Removes *.old files left by an update or uninstall while the helper was running (at start-up).</summary>
    public static void CleanOld(string appDir)
    {
        string d = Dir(appDir);
        if (!Directory.Exists(d)) return;
        foreach (var f in Directory.EnumerateFiles(d, "*.old", SearchOption.AllDirectories))
            try { System.IO.File.Delete(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var sub in new[] { HelperFolder, FfmpegFolder, ModelsFolder })
        {
            string s = Path.Combine(d, sub);
            try { if (Directory.Exists(s) && !Directory.EnumerateFileSystemEntries(s).Any()) Directory.Delete(s); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Deletes the downloaded parts (helper, ffmpeg, models, installed.json); module.json and the changelog
    /// stay, so the module is still listed with Install. False when something was in use (it goes at the next start).</summary>
    public static bool Uninstall(string appDir)
    {
        bool clean = true;
        string d = Dir(appDir);
        try { System.IO.File.Delete(InstalledFile(appDir)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { clean = false; }
        foreach (var sub in new[] { HelperFolder, FfmpegFolder, ModelsFolder, ".download" })
        {
            string s = Path.Combine(d, sub);
            if (!Directory.Exists(s)) continue;
            foreach (var f in Directory.EnumerateFiles(s, "*", SearchOption.AllDirectories).ToList())
                try { MoveAside(f); if (System.IO.File.Exists(f)) clean = false; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { clean = false; }
            if (Directory.EnumerateFiles(s, "*", SearchOption.AllDirectories).Any()) clean = false;
            else try { Directory.Delete(s, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return clean;
    }

    // ------------------------------------------------------------------ Claude Code
    /// <summary>claude.exe: on PATH, the native installer's %USERPROFILE%\.local\bin, or npm's folder.</summary>
    public static string? FindClaude()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Concat([Path.Combine(home, ".local", "bin"), Path.Combine(appData, "npm")]);
        foreach (var dir in dirs)
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                try { string f = Path.Combine(dir.Trim().Trim('"'), name); if (System.IO.File.Exists(f)) return f; }
                catch (ArgumentException) { }
            }
        return null;
    }

    /// <summary>The arguments that register the helper in Claude Code (all projects of this Windows user).</summary>
    public static string[] AddArgs(string appDir) =>
        ["mcp", "add", ServerName, "--scope", "user", "--", HelperExe(appDir), "mcp", "--app", appDir, "--tools", Path.Combine(appDir, "library")];

    /// <summary>The command line for the user to copy when claude.exe is not found.</summary>
    public static string AddCommand(string appDir) =>
        "claude " + string.Join(" ", AddArgs(appDir).Select(a => a.Contains(' ') || a.Contains('\\') ? $"\"{a}\"" : a));

    static (int Code, string Out) RunClaude(string claude, params string[] args)
    {
        var psi = new ProcessStartInfo(claude) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException("claude did not start");
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(60000)) { try { p.Kill(true); } catch (InvalidOperationException) { } throw new IOException("claude did not answer"); }
        return (p.ExitCode, (o + "\n" + err.Result).Trim());
    }

    /// <summary>What Claude Code has under "clipshrinker": null = nothing, else the text of `claude mcp get`.</summary>
    public static string? Registered(string claude)
    {
        var (code, o) = RunClaude(claude, "mcp", "get", ServerName);
        return code == 0 ? o : null;
    }

    /// <summary>Registers this install's helper (an older "clipshrinker" entry is replaced). Returns claude's reply.</summary>
    public static string Register(string claude, string appDir)
    {
        RunClaude(claude, "mcp", "remove", ServerName, "-s", "user");
        var (code, o) = RunClaude(claude, AddArgs(appDir));
        if (code != 0) throw new IOException(o.Length > 0 ? o : $"claude mcp add ended with code {code}");
        return o;
    }

    /// <summary>Removes the "clipshrinker" entry when it points at this install's helper. True when it was removed.</summary>
    public static bool Unregister(string claude, string appDir)
    {
        if (Registered(claude) is not { } info || !info.Contains(HelperExe(appDir), StringComparison.OrdinalIgnoreCase)) return false;
        return RunClaude(claude, "mcp", "remove", ServerName, "-s", "user").Code == 0;
    }
}
