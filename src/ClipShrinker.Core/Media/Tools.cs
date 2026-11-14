using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ClipShrinker.Core.Ffmpeg;

namespace ClipShrinker.Core.Media;

/// <summary>Finds ffmpeg / ffprobe and runs short helper commands (reference find_tool, probe, _check_tools).</summary>
public sealed class Tools
{
    public string? Ffmpeg { get; }
    public string? Ffprobe { get; }

    public Tools(string? ffmpeg, string? ffprobe)
    {
        Ffmpeg = ffmpeg;
        Ffprobe = ffprobe;
    }

    /// <summary>name.exe in one of the folders (first match), else on PATH (reference find_tool).</summary>
    public static string? Find(string name, params string?[] folders)
    {
        foreach (var d in folders)
        {
            if (string.IsNullOrEmpty(d)) continue;
            var p = Path.Combine(d, name + ".exe");
            if (File.Exists(p)) return Path.GetFullPath(p);
        }
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            try
            {
                var p = Path.Combine(d.Trim('"'), name + ".exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static Tools Locate(params string?[] folders) => new(Find("ffmpeg", folders), Find("ffprobe", folders));

    /// <summary>Runs a command to the end; returns exit code, stdout and stderr (reference run_capture).</summary>
    public static (int Code, string Out, string Err) Capture(IReadOnlyList<string> cmd)
    {
        var psi = new ProcessStartInfo(cmd[0])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, err.Result);
    }

    /// <summary>True when this ffmpeg has the h264_nvenc encoder (the app warns otherwise).</summary>
    public bool HasNvenc()
    {
        if (Ffmpeg is null) return false;
        try { return Capture([Ffmpeg, "-hide_banner", "-encoders"]).Out.Contains("h264_nvenc"); }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>ffprobe a file (reference probe()). Throws MediaException with ffprobe's message.</summary>
    public MediaInfo Probe(string path)
    {
        if (Ffprobe is null) throw new MediaException("ffprobe.exe was not found.");
        var (code, output, err) = Capture([Ffprobe, "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path]);
        if (code != 0) throw new MediaException(err.Trim() is { Length: > 0 } e ? e : "ffprobe failed");
        return ParseProbe(output, () => new FileInfo(path).Length);
    }

    /// <summary>The probe() parsing, separate so it can be tested with saved ffprobe output.</summary>
    public static MediaInfo ParseProbe(string json, Func<long> fileSize)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var streams = root.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
        string Type(JsonElement x) => x.TryGetProperty("codec_type", out var t) ? t.GetString() ?? "" : "";
        var v = streams.FirstOrDefault(x => Type(x) == "video");
        if (v.ValueKind != JsonValueKind.Object) throw new MediaException("No video stream found.");
        int naudio = Math.Min(C.MaxAudio, streams.Count(x => Type(x) == "audio"));
        string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
        // Python "x or y": an empty string counts as missing
        string? Or(params string?[] xs) => xs.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        var fmt = root.GetProperty("format");
        string? trc = Str(v, "color_transfer");
        double ParseNum(string? x) => x is null ? 0 : Py.Py.ParseFloat(x) ?? throw new MediaException($"could not convert string to float: '{x}'");
        return new MediaInfo(
            W: v.GetProperty("width").GetInt32(),
            H: v.GetProperty("height").GetInt32(),
            Fps: Geometry.ParseFps(Or(Str(v, "avg_frame_rate"), Str(v, "r_frame_rate") ?? "30/1")),
            Dur: ParseNum(Or(Str(fmt, "duration"), Str(v, "duration"))),
            NAudio: naudio,
            Bitrate: ParseNum(Or(Str(fmt, "bit_rate"))),
            Size: Or(Str(fmt, "size")) is { } sz ? long.Parse(sz) : fileSize(),
            Trc: trc is "smpte2084" or "arib-std-b67" ? trc : null,
            Prim: Str(v, "color_primaries"),
            Space: Str(v, "color_space"));
    }
}

public sealed class MediaException(string message) : Exception(message);
