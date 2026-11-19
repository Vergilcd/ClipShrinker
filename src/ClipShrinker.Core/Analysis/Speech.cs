using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClipShrinker.Core.Agent;

namespace ClipShrinker.Core.Analysis;

/// <summary>2.8 AI editing module: what is said in a clip, through ffmpeg's "whisper" filter (whisper.cpp built into the
/// bundled ffmpeg, CPU). The model files are downloaded into the module's models folder on first use.</summary>
public static partial class Speech
{
    public const string ModelFile = "ggml-large-v3-turbo-q5_0.bin";
    public const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + ModelFile;
    public const string ModelSize = "about 550 MB";
    /// <summary>Voice activity detection (Silero, ~1 MB): cuts the sound at pauses, so sentences are not split.</summary>
    public const string VadFile = "ggml-silero-v5.1.2.bin";
    public const string VadUrl = "https://huggingface.co/ggml-org/whisper-vad/resolve/main/" + VadFile;

    public sealed record Line(double Start, double End, string Text);

    /// <summary>Null when both model files are there; otherwise starts their download and says how far it is.</summary>
    public static string? EnsureModels(string modelDir)
    {
        string model = Path.Combine(modelDir, ModelFile), vad = Path.Combine(modelDir, VadFile);
        if (File.Exists(model) && File.Exists(vad)) return null;
        var parts = new List<string>();
        foreach (var (file, url, path) in new[] { (VadFile, VadUrl, vad), (ModelFile, ModelUrl, model) })
        {
            if (File.Exists(path)) continue;
            var job = Downloads.Background(url, path);
            if (job.Finished && job.Error is { } err)
                return $"The speech model download failed ({err}). Call transcribe again to retry.";
            if (file == ModelFile)
                parts.Add($"Downloading the speech model ({ModelSize}, once): {job.Percent} % " +
                          $"({job.Done / 1048576} of {(job.Total > 0 ? job.Total / 1048576 + " MB" : "? MB")}).");
        }
        return (parts.Count > 0 ? parts[0] : "Downloading the speech model...") +
               " Call transcribe again in a minute (look at frames or do other edits meanwhile).";
    }

    /// <summary>The words spoken between from and to (source seconds) as timed lines. tracks = the clip's audio track
    /// count (all are mixed, so the voice is found whether the mic was recorded on its own track or not).
    /// language = "auto" or a code such as "en" / "hu".</summary>
    public static List<Line> Transcribe(string ffmpeg, string modelDir, string path, int tracks, double from, double to, string language)
    {
        if (tracks < 1) throw new AgentToolError("This clip has no sound.");
        string outName = $"transcript-{Environment.ProcessId}-{Guid.NewGuid():N}.srt";
        string outPath = Path.Combine(modelDir, outName);
        // Relative file names (ffmpeg runs in the models folder): a Windows path's "C:" would need filter escaping.
        string lang = LangRx().IsMatch(language) ? language.ToLowerInvariant() : "auto";
        string whisper = $"whisper=model={ModelFile}:language={lang}:queue=29:use_gpu=0:vad_model={VadFile}" +
                         $":destination={outName}:format=srt";
        string mix = tracks == 1 ? "[0:a:0]" : string.Concat(Enumerable.Range(0, tracks).Select(i => $"[0:a:{i}]")) +
                                               $"amix=inputs={tracks}:normalize=0,";
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-v", "error",
            "-ss", F(from), "-to", F(to), "-i", path,
            "-filter_complex", $"{mix}{whisper}[a]", "-map", "[a]", "-f", "null", "-",
        };
        var psi = new ProcessStartInfo(ffmpeg)
        {
            WorkingDirectory = modelDir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi) ?? throw new IOException("ffmpeg did not start");
            var err = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            string e = err.Result.Trim();
            if (p.ExitCode != 0)
                throw new AgentToolError("Speech recognition failed: " + (e.Length > 400 ? e[^400..] : e));
            return File.Exists(outPath) ? ParseSrt(File.ReadAllText(outPath, Encoding.UTF8), from) : [];
        }
        finally { try { File.Delete(outPath); } catch (IOException) { } }
    }

    /// <summary>SRT -> lines, moved by offset (ffmpeg's times start at 0 at "from").</summary>
    public static List<Line> ParseSrt(string srt, double offset)
    {
        var res = new List<Line>();
        foreach (Match m in SrtRx().Matches(srt.Replace("\r\n", "\n")))
        {
            string text = m.Groups[3].Value.Trim().Replace('\n', ' ');
            if (text.Length == 0 || NoiseRx().IsMatch(text)) continue;
            res.Add(new Line(offset + Time(m.Groups[1].Value), offset + Time(m.Groups[2].Value), text));
        }
        return res;
    }

    static double Time(string t) => TimeSpan.ParseExact(t.Replace('.', ','), @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture).TotalSeconds;

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d+\n(\d\d:\d\d:\d\d[,.]\d\d\d) --> (\d\d:\d\d:\d\d[,.]\d\d\d)\n(.*?)(?:\n\n|\n?\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex SrtRx();

    /// <summary>Silence markers and lines of only punctuation. ([Music], (gunshots) and the like are kept: they can
    /// help, though whisper often guesses them.)</summary>
    [GeneratedRegex(@"^(\[BLANK_AUDIO\]|\[SILENCE\]|[\s\p{P}]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRx();

    [GeneratedRegex(@"^[a-zA-Z]{2,3}$")]
    private static partial Regex LangRx();
}
