using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ClipShrinker.Core.Analysis;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Core.Agent;

/// <summary>2.8 tools: export settings and file names, sounds (download and place), speech to text.</summary>
public static partial class AgentTools
{
    public static JsonArray List()
    {
        var all = BaseList();
        foreach (var t in MoreList()) all.Add(t);
        return all;
    }

    static IEnumerable<JsonObject> MoreList() =>
    [
        Tool("export_settings", "The export settings with their allowed values, the file name rules (prefix / suffix and the " +
             "labels they can hold) and the name the next export gets.", Schema()),
        Tool("set_export_settings", "Change export settings (the same ones as in the app's Export panel; give only what " +
             "changes). Values are matched to the allowed ones listed by export_settings. Not an undo step.",
             Schema(("size", "number", "Max file size in MB per file (used while bitrate is empty)", false),
                    ("bitrate", "string", "Fixed video bitrate in Mbps (e.g. \"20\"); \"\" = use the size limit instead", false),
                    ("resolution", "string", "e.g. Auto, 1080p, 1440p, 4K, 720p, 50% (Original with a bitrate)", false),
                    ("codec", "string", "e.g. HEVC or H.264 (one the PC offers)", false),
                    ("crop", "string", "Aspect ratio crop, e.g. 16:9, 9:16, 1:1, Original", false),
                    ("audio_bitrate", "string", "Original, 128k, 192k, 256k, 320k", false),
                    ("audio_tracks", "string", "\"mix\" (one track) or \"separate\" (keep the tracks)", false),
                    ("join_mode", "string", "\"join\" (one file) or \"separate\" (one file per clip)", false),
                    ("transition", "string", "Transition between joined clips (a name from export_settings, or None)", false),
                    ("transition_length", "number", "Seconds, 0.1 to 3", false),
                    ("convert_hdr", "boolean", "Convert HDR clips to normal (SDR) colours", false),
                    ("mute_mic", "boolean", "Leave the microphone track out", false),
                    ("prefix", "string", "Text before the clip name; may hold labels like {date} or {res}", false),
                    ("suffix", "string", "Text after the clip name; may hold labels", false),
                    ("keep_name", "boolean", "Keep the clip's own name in the file name", false),
                    ("export_folder", "string", "Folder for exports (\"\" = ask at export); only with the app open", false))),
        Tool("sound_library", "The sound files in the user's Sounds library (Sound track module), with their lengths.", Schema()),
        Tool("download_sound", "Download a sound effect or music clip from a direct https link into the Sounds library. Find " +
             "the link on a free sound site first (the file itself, e.g. ending in .mp3 / .wav / .ogg, not the web page). " +
             "At most 40 MB.",
             Schema(("url", "string", "Direct https link to the sound file", true),
                    ("name", "string", "File name to save it as (no extension), e.g. \"airhorn\"", false),
                    ("license", "string", "The licence / source page, kept in the library's sources.txt for credits", false))),
        Tool("add_sound", "Put a sound on a clip's Sounds lane (one undo step in the app). at = clip source seconds where it " +
             "starts. It plays at its own speed, also inside speed sections.",
             Schema(("clip", "integer", "Clip number", true),
                    ("sound", "string", "A Sounds library file name (from sound_library / download_sound) or a full path", true),
                    ("at", "number", "Start in the clip's source seconds (default: the trim start)", false),
                    ("length", "number", "How many seconds of the sound play (default: all of it)", false),
                    ("offset", "number", "Start this many seconds into the sound file (default 0)", false),
                    ("volume_db", "integer", "-20..20 dB (default 0)", false),
                    ("fade", "number", "Fade in and out, seconds (default 0)", false))),
        Tool("remove_sounds", "Take sounds off a clip: one (its number in clip_info) or all of them.",
             Schema(("clip", "integer", "Clip number", true), ("sound", "integer", "Sound number (default: all)", false))),
        Tool("transcribe", "What is said in a clip (speech to text, any language), as lines with their source times. " +
             "Default: the kept part. The first call starts downloading the speech model (about 550 MB) and returns; " +
             "call again later. It runs on the CPU and takes about as long as the sound itself (5 minutes of sound: about " +
             "5 minutes), so give from / to around the part that matters. Results are kept, so asking again is instant. " +
             "Game noises come out as *gunshot* and the like, often wrong; trust the spoken words.",
             Schema(("clip", "integer", "Clip number", true), ("from", "number", "Start in seconds (default: trim start)", false),
                    ("to", "number", "End in seconds (default: trim end)", false),
                    ("language", "string", "auto (default) or a code: en, hu, de, ...", false))),
        Tool("guide", "Step-by-step guides for bigger jobs, learned from real clips. Read the one for the job before " +
             "starting it. Without a topic: the list of guides.",
             Schema(("topic", "string", "A guide's name, e.g. \"subtitles\"", false))),
    ];

    // ------------------------------------------------------------------ guides (2.8.0-beta3)
    /// <summary>The guides embedded from Agent/Guides/*.md (resource names "guides/&lt;topic&gt;.md").</summary>
    public static IReadOnlyList<string> GuideTopics() =>
        typeof(AgentTools).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("guides/", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => n["guides/".Length..^".md".Length]).Order().ToList();

    public static string? GuideText(string topic)
    {
        using var s = typeof(AgentTools).Assembly.GetManifestResourceStream("guides/" + topic.Trim().ToLowerInvariant() + ".md");
        return s is null ? null : new StreamReader(s, Encoding.UTF8).ReadToEnd();
    }

    static JsonObject GuideTool(JsonObject a)
    {
        var topics = GuideTopics();
        if (OptStr(a, "topic") is not { Length: > 0 } topic)
            return Content([Text("Guides: " + string.Join(", ", topics) + ". Call guide with one of them as topic.")]);
        return GuideText(topic) is { } text ? Content([Text(text)])
            : throw new AgentToolError($"No guide \"{topic}\". Guides: {string.Join(", ", topics)}.");
    }

    // ------------------------------------------------------------------ arguments
    static string? OptStr(JsonObject a, string k) => a[k] is JsonValue v && v.TryGetValue(out string? s) ? s
                                                      : Num(a, k) is { } d ? d.ToString(CultureInfo.InvariantCulture) : null;
    static bool? OptBool(JsonObject a, string k) => a[k] is not JsonValue v ? null
        : v.TryGetValue(out bool b) ? b
        : v.TryGetValue(out string? s) ? s.Trim().ToLowerInvariant() switch { "true" or "yes" or "on" or "1" => true, "false" or "no" or "off" or "0" => false, _ => null }
        : null;

    /// <summary>The allowed value that matches what the agent wrote: same text, else the only one containing it.</summary>
    static string Pick(string what, string given, IEnumerable<string> allowed)
    {
        var list = allowed.ToList();
        string g = given.Trim();
        if (list.FirstOrDefault(x => string.Equals(x, g, StringComparison.OrdinalIgnoreCase)) is { } same) return same;
        static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var hits = list.Where(x => Norm(x).Contains(Norm(g)) && Norm(g).Length > 0).ToList();
        if (hits.Count >= 1 && (hits.Count == 1 || hits.Select(Norm).Distinct().Count() == 1 || hits.All(x => Norm(x).StartsWith(Norm(g)))))
            return hits[0];
        throw new AgentToolError($"{what} \"{given}\" is not one of: {string.Join(", ", list)}.");
    }

    // ------------------------------------------------------------------ export settings
    static string ExportSettingsText(IAgentHost h)
    {
        var s = h.State;
        var clips = h.Clips;
        bool bitrate = s.BitrateMbps() is not null;
        string stem = clips.Count > 0 ? Path.GetFileNameWithoutExtension(clips[0].Path) : "clip";
        var sb = new StringBuilder("Export settings (the app's Export panel; export uses them):\n");
        sb.AppendLine($"- size: {s.Size} MB per file" + (bitrate ? " (ignored: a fixed bitrate is set)" : ""));
        sb.AppendLine($"- bitrate: {(bitrate ? s.Bitrate + " Mbps (fixed)" : "empty (the size limit decides)")}");
        sb.AppendLine($"- resolution: {s.Res} (allowed now: {string.Join(", ", bitrate ? C.ResBitrateMode : C.ResSizeMode)})");
        sb.AppendLine($"- codec: {s.Codec} ({(h.Live ? "the ones that work on this PC" : "allowed")}: {string.Join(", ", h.Codecs)})");
        sb.AppendLine($"- crop: {s.Crop} (allowed: {string.Join(", ", C.Aspects.Select(x => x.Name))})");
        sb.AppendLine($"- audio_bitrate: {s.Audio} (allowed: {string.Join(", ", AppState.AudioRates)})");
        sb.AppendLine($"- audio_tracks: {s.Atrk}; mute_mic: {(s.MuteMic ? "yes" : "no")}");
        sb.AppendLine($"- join_mode: {s.Mode} (with several clips: join = one file, separate = one file per clip)");
        sb.AppendLine($"- transition: {s.Trans}, {s.Tlen} s (allowed: {string.Join(", ", C.Transitions.Select(x => x.Name))})");
        sb.AppendLine($"- convert_hdr: {(s.Hdr ? "yes" : "no")}");
        if (h.ExportFolder is { } f) sb.AppendLine($"- export_folder: {(f.Trim().Length > 0 ? f : "empty (asks at export)")}");
        sb.AppendLine($"File names: prefix \"{s.Prefix}\", suffix \"{s.Suffix}\", keep_name {(s.KeepName ? "yes" : "no")} " +
                      "(name = prefix_clipname_suffix; without keep_name only prefix_suffix).");
        sb.AppendLine("Labels the prefix / suffix can hold (filled in at export): " +
                      string.Join("; ", NameTokens.All.Select(t => $"{t.Token} = {t.Help}")) + ".");
        sb.Append($"The next export of clip 1 is named: {s.OutStem(stem)}.mp4");
        return sb.ToString();
    }

    static JsonObject SetExportSettings(IAgentHost h, JsonObject a)
    {
        var s = h.State;
        var changed = new List<string>();
        // work everything out first, so that a wrong value changes nothing
        var todo = new List<Action>();
        void Do(string label, Action act) { changed.Add(label); todo.Add(act); }

        if (Num(a, "size") is { } size)
        {
            if (!(size > 0) || size > 100_000) throw new AgentToolError("size must be a number of MB above 0.");
            Do($"size {size:0.##} MB", () => s.Size = size.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (OptStr(a, "bitrate") is { } br)
        {
            string b = br.Trim().Replace(",", ".").Replace("Mbps", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (b.Length > 0 && !(double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) && mb > 0 && mb <= 500))
                throw new AgentToolError("bitrate must be Mbps (e.g. 20), or \"\" to use the size limit.");
            Do(b.Length > 0 ? $"bitrate {b} Mbps" : "bitrate cleared (size limit)", () => { s.Bitrate = b; s.BitrateChanged(); });
        }
        if (OptStr(a, "codec") is { } codec)
        {
            string c = Pick("codec", codec, h.Codecs);
            Do("codec " + c, () => s.Codec = c);
        }
        if (OptStr(a, "crop") is { } crop)
        {
            string c = Pick("crop", crop, C.Aspects.Select(x => x.Name));
            Do("crop " + c, () => s.Crop = c);
        }
        if (OptStr(a, "audio_bitrate") is { } ab)
        {
            string v = Pick("audio_bitrate", ab.EndsWith('k') || ab.Equals("original", StringComparison.OrdinalIgnoreCase) ? ab : ab + "k", AppState.AudioRates);
            Do("audio " + v, () => s.Audio = v);
        }
        if (OptStr(a, "audio_tracks") is { } at)
        {
            string v = at.Trim().ToLowerInvariant() switch
            {
                "mix" or "one" or "mixed" => AppState.AudioTrackModes[1],
                "separate" or "keep" => AppState.AudioTrackModes[0],
                _ => Pick("audio_tracks", at, AppState.AudioTrackModes),
            };
            Do("audio tracks: " + v, () => s.Atrk = v);
        }
        if (OptStr(a, "join_mode") is { } jm)
        {
            string v = Pick("join_mode", jm, AppState.ExportModes);
            Do("mode " + v, () => s.Mode = v);
        }
        if (OptStr(a, "transition") is { } tr)
        {
            string v = Pick("transition", tr, C.Transitions.Select(x => x.Name));
            Do("transition " + v, () => s.Trans = v);
        }
        if (Num(a, "transition_length") is { } tl)
        {
            double t = Math.Clamp(tl, 0.1, 3);
            Do($"transition length {t:0.##} s", () => s.Tlen = t.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (OptBool(a, "convert_hdr") is { } hdr) Do("convert HDR " + (hdr ? "on" : "off"), () => s.Hdr = hdr);
        if (OptBool(a, "mute_mic") is { } mm) Do("mute mic " + (mm ? "on" : "off"), () => s.MuteMic = mm);
        if (OptStr(a, "prefix") is { } pre) Do($"prefix \"{pre}\"", () => s.Prefix = pre);
        if (OptStr(a, "suffix") is { } suf) Do($"suffix \"{suf}\"", () => s.Suffix = suf);
        if (OptBool(a, "keep_name") is { } kn) Do("keep name " + (kn ? "on" : "off"), () => s.KeepName = kn);
        // resolution last: the bitrate decides which list it comes from
        string? res = OptStr(a, "resolution");
        string? folder = OptStr(a, "export_folder");
        if (folder is { Length: > 0 } && h.ExportFolder is null)
            throw new AgentToolError("The export folder is an app setting: open Clip Shrinker, or give out to export instead.");
        if (changed.Count == 0 && res is null && folder is null) throw new AgentToolError("Give at least one setting to change.");

        string before = ExportSummary(s);
        h.Settings("export settings", () =>
        {
            foreach (var act in todo) act();
            if (res is not null)
            {
                string v = Pick("resolution", res, s.BitrateMbps() is null ? C.ResSizeMode : C.ResBitrateMode);
                s.Res = v;
                changed.Add("resolution " + v);
            }
        });
        if (folder is not null)
        {
            if (folder.Trim().Length > 0) Directory.CreateDirectory(folder);
            h.ExportFolder = folder.Trim();
            changed.Add(folder.Trim().Length > 0 ? "export folder " + folder.Trim() : "export folder cleared");
        }
        return Content([Text($"Changed: {string.Join(", ", changed)}.\nBefore: {before}\nNow: {ExportSummary(s)}\n" +
                             $"Next file name: {s.OutStem(h.Clips.Count > 0 ? Path.GetFileNameWithoutExtension(h.Clips[0].Path) : "clip")}.mp4")]);
    }

    static string ExportSummary(AppState s) =>
        $"{(s.BitrateMbps() is { } b ? $"{b:0.##} Mbps" : $"max {s.Size} MB")}, {s.Res}, {s.Codec}, crop {s.Crop}, audio {s.Audio} ({s.Atrk}), " +
        $"{s.Mode}, transition {s.Trans} {s.Tlen} s, HDR {(s.Hdr ? "converted" : "kept")}, prefix \"{s.Prefix}\", suffix \"{s.Suffix}\", " +
        $"keep name {(s.KeepName ? "yes" : "no")}";

    // ------------------------------------------------------------------ sounds
    static string SoundFolder(IAgentHost h) => Path.Combine(h.AppDir, "modules", Modules.ModuleIds.SoundTrack, "sounds");

    static string SoundModuleNote(IAgentHost h) => h.ModuleOn(Modules.ModuleIds.SoundTrack) == false
        ? "\nThe Sound track module is switched OFF: the sounds are kept but not played or exported until the user switches it on (Modules)."
        : "";

    static JsonObject SoundLibrary(IAgentHost h)
    {
        string dir = SoundFolder(h);
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Where(Sounds.IsSoundFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList() : [];
        if (files.Count == 0) return Content([Text($"The Sounds library ({dir}) is empty. download_sound adds sounds." + SoundModuleNote(h))]);
        var sb = new StringBuilder($"{files.Count} sound(s) in {dir}:\n");
        foreach (var f in files.Take(200))
        {
            string len;
            try { len = $"{h.Tools.ProbeSoundDuration(f):0.##} s"; }
            catch (Exception e) when (e is Media.MediaException or IOException or System.Text.Json.JsonException) { len = "unreadable"; }
            sb.AppendLine($"- {Path.GetFileName(f)} ({len})");
        }
        return Content([Text(sb.ToString().TrimEnd() + SoundModuleNote(h))]);
    }

    static JsonObject DownloadSound(IAgentHost h, JsonObject a)
    {
        string url = Str(a, "url");
        string dir = SoundFolder(h);
        string path = Downloads.Sound(url, dir, OptStr(a, "name"));
        double dur;
        try { dur = h.Tools.ProbeSoundDuration(path); }
        catch (Exception e) when (e is Media.MediaException or IOException or System.Text.Json.JsonException)
        {
            File.Delete(path);
            throw new AgentToolError("The downloaded file is not readable sound (deleted). Find the direct link to the audio file.");
        }
        Downloads.NoteSource(dir, path, url, OptStr(a, "license"));
        return Content([Text($"Saved {Path.GetFileName(path)} ({dur:0.##} s) in the Sounds library. Place it with add_sound " +
                             $"(sound: \"{Path.GetFileName(path)}\")." + SoundModuleNote(h))]);
    }

    static string FindSound(IAgentHost h, string given)
    {
        if (Path.IsPathRooted(given))
            return File.Exists(given) && Sounds.IsSoundFile(given) ? given : throw new AgentToolError("Not a sound file: " + given);
        string dir = SoundFolder(h);
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Where(Sounds.IsSoundFile).ToList() : [];
        return files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), given, StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), given, StringComparison.OrdinalIgnoreCase))
            ?? throw new AgentToolError($"No sound \"{given}\" in the Sounds library: see sound_library, or download_sound first.");
    }

    static JsonObject AddSound(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        string path = FindSound(h, Str(a, "sound"));
        double fileDur = h.Tools.ProbeSoundDuration(path);
        double at = Num(a, "at") ?? r.Start;
        if (at < 0 || at >= r.Info.Dur - Sounds.MinLen) throw new AgentToolError($"at must be within the clip (0-{r.Info.Dur:0.##} s).");
        double offset = Math.Clamp(Num(a, "offset") ?? 0, 0, Math.Max(0, fileDur - Sounds.MinLen));
        double len = Math.Min(Num(a, "length") ?? fileDur, fileDur - offset);
        if (len < Sounds.MinLen) throw new AgentToolError("length must be at least 0.1 s.");
        int vol = Sounds.ClampVolume(Int(a, "volume_db") ?? 0);
        var block = new SoundBlock { Path = path, At = at, In = offset, Len = len, FileDur = fileDur, VolumeDb = vol };
        block.Fade = Sounds.ClampFade(Num(a, "fade") ?? 0, len);
        h.Edit("add sound", () => r.Sounds.Add(block));
        string note = at + len <= r.Start || at >= r.End ? $"\nNote: it lies outside the kept part ({r.Start:0.##}-{r.End:0.##} s), so it is not heard." : "";
        return Content([Text(Saved(h) + Info(h, r) + note + SoundModuleNote(h))]);
    }

    static JsonObject RemoveSounds(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        if (r.Sounds.Count == 0) throw new AgentToolError("That clip has no sounds.");
        var ordered = r.Sounds.OrderBy(b => b.At).ToList();
        if (Int(a, "sound") is { } n)
        {
            if (n < 1 || n > ordered.Count) throw new AgentToolError($"There is no sound {n}: the clip has {ordered.Count}.");
            h.Edit("remove sound", () => r.Sounds.Remove(ordered[n - 1]));
        }
        else h.Edit("remove sounds", () => r.Sounds.Clear());
        return Content([Text(Saved(h) + Info(h, r))]);
    }

    // ------------------------------------------------------------------ speech
    static readonly Dictionary<string, List<Speech.Line>> transcripts = [];

    static JsonObject Transcribe(IAgentHost h, JsonObject a)
    {
        var r = ClipArg(h, a);
        if (r.Info.NAudio == 0) throw new AgentToolError("This clip has no sound.");
        // 2.8: the whisper build comes with the AI editing module (modules\ai-agent\ffmpeg); library\ffmpeg stays the base build
        string ffmpeg = Modules.AiModule.SpeechFfmpeg(h.AppDir) ?? (h.Tools.HasFilter("whisper") ? h.Tools.Ffmpeg : null)
            ?? throw new AgentToolError("Speech to text is not installed: open Clip Shrinker > Modules > AI editing and click Install " +
                                        "(or Update) to get the ffmpeg with speech recognition.");
        double from = Math.Max(0, Num(a, "from") ?? r.Start), to = Math.Min(r.Info.Dur, Num(a, "to") ?? r.End);
        if (to - from < 0.5) throw new AgentToolError("from must be at least 0.5 s before to.");
        string lang = OptStr(a, "language") is { Length: > 0 } l ? l : "auto";
        string modelDir = Modules.AiModule.ModelsDir(h.AppDir);
        if (Speech.EnsureModels(modelDir) is { } waiting) return Content([Text(waiting)]);

        string key = $"{r.Path}|{File.GetLastWriteTimeUtc(r.Path).Ticks}|{from:0.###}|{to:0.###}|{lang}";
        List<Speech.Line> lines;
        lock (transcripts)
            transcripts.TryGetValue(key, out lines!);
        if (lines is null)
        {
            lines = Speech.Transcribe(ffmpeg, modelDir, r.Path, r.Info.NAudio, from, to, lang);
            lock (transcripts) transcripts[key] = lines;
        }
        if (lines.Count == 0) return Content([Text($"No speech found between {ClipAnalysis.Clock(from)} and {ClipAnalysis.Clock(to)}.")]);
        var sb = new StringBuilder($"Speech {ClipAnalysis.Clock(from)}-{ClipAnalysis.Clock(to)} (source times; whisper can mishear game noise):\n");
        foreach (var x in lines) sb.AppendLine($"{ClipAnalysis.Clock(x.Start)}-{ClipAnalysis.Clock(x.End)} ({x.Start:0.#}-{x.End:0.#} s): {x.Text}");
        return Content([Text(sb.ToString().TrimEnd())]);
    }
}
