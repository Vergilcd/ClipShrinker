using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core;

/// <summary>Time texts, option-label lookups and key-chord labels (reference lines 340-377, 600-608, 781-900).</summary>
public static class Text
{
    /// <summary>"3 minutes 53 seconds", "1 hour 2 minutes 5 seconds", "12 seconds".</summary>
    public static string FmtElapsed(double sec)
    {
        long s = Round(Max(0.0, sec));
        long h = s / 3600, m = s % 3600 / 60, s2 = s % 60;
        static string Unit(long n, string w) => I(n) + " " + w + (n == 1 ? "" : "s");
        var parts = new List<string>();
        if (h != 0) parts.Add(Unit(h, "hour"));
        if (h != 0 || m != 0) parts.Add(Unit(m, "minute"));
        parts.Add(Unit(s2, "second"));
        return string.Join(" ", parts);
    }

    /// <summary>"m:ss.ss" or "h:mm:ss.ss".</summary>
    public static string FmtTime(double t)
    {
        t = Max(0.0, t);
        var (mf, s) = DivMod(t, 60);
        long mi = Int(mf);
        long h = FloorDiv(mi, 60), m = mi - h * 60;
        string ss = F(s, 2).PadLeft(5, '0');
        return h != 0 ? $"{I(h)}:{m:00}:{ss}" : $"{I(m)}:{ss}";
    }

    /// <summary>'83.5', '1:23.5', '1:02:03' -> seconds (null when it is not a time).</summary>
    public static double? ParseClock(string txt)
    {
        txt = txt.Trim().Replace(",", ".").Replace(";", ":");
        if (txt.Length == 0) return null;
        var parts = new List<double>();
        foreach (var p in txt.Split(':'))
        {
            var v = ParseFloat(p);
            if (v is null) return null;
            parts.Add(v.Value);
        }
        if (parts.Count > 3 || parts.Any(x => x < 0)) return null;
        double t = 0.0;
        foreach (var x in parts) t = t * 60 + x;
        return t;
    }

    /// <summary>Timeline tick label: "m:ss" or "h:mm:ss".</summary>
    public static string FmtTick(double t)
    {
        long ti = Round(t);
        long h = FloorDiv(ti, 3600), rem = ti - h * 3600;
        long m = FloorDiv(rem, 60), sec = rem - m * 60;
        return h != 0 ? $"{I(h)}:{m:00}:{sec:00}" : $"{I(m)}:{sec:00}";
    }

    public static string CodecKey(string label) => Media.Encoders.KeyOf(label);
    /// <summary>2.6: the audio bitrate per track to encode with. "Original" = the highest source audio bitrate of the clips,
    /// raised to the next AAC step (64k .. 320k, so 125k -> 128k); 192k when no clip reports one.</summary>
    public static string ResolveAudioK(string audioK, IEnumerable<ExportClip> clips)
    {
        if (audioK != "Original") return audioK;
        double src = clips.Select(c => c.AudioBitrate).DefaultIfEmpty(0).Max() / 1000;
        if (src <= 0) return "192k";
        int[] steps = [64, 96, 128, 160, 192, 256, 320];
        return $"{steps.FirstOrDefault(s => s >= src * 0.98, 320).ToString(System.Globalization.CultureInfo.InvariantCulture)}k";
    }

    public static string CodecShort(string label) => label.Contains("HEVC") ? "HEVC" : "H.264";

    public static string AudKey(string label) =>
        label == C.FastAudio[0] || label == C.SlowAudio[0] ? "fx" :
        label == C.FastAudio[1] || label == C.SlowAudio[1] ? "normal" : "mute";

    public static string InterpKey(string label) =>
        label == C.SlowInterp[2] ? "rife" : label == C.SlowInterp[1] ? "blend" : "repeat";

    public static string? XfadeMode(string? x) => x == C.Glitch ? "fadefast" : x;

    public static double ClampSpeed(double v) => Max(C.SpeedMin, Min(C.SpeedMax, v));

    /// <summary>"ctrl+shift+s" -> "Ctrl + Shift + S".</summary>
    public static string ChordLabel(string ch)
    {
        var parts = ch.Split('+');
        string key = parts[^1];
        string shown = C.KeyShown.TryGetValue(key, out var k) ? k
            : key.Length == 1 ? key.ToUpperInvariant()
            : key.Length <= 3 ? key.ToUpperInvariant() : Title(key);
        var mods = parts[..^1].Select(m => m switch
        {
            "ctrl" => "Ctrl", "alt" => "Alt", "shift" => "Shift",
            _ => throw new KeyNotFoundException(m),
        });
        return string.Join(" + ", mods.Append(shown));
    }

    static readonly Dictionary<char, string> Keysyms = new()
    {
        [' '] = "space", ['!'] = "exclam", ['"'] = "quotedbl", ['#'] = "numbersign", ['$'] = "dollar", ['%'] = "percent",
        ['&'] = "ampersand", ['\''] = "apostrophe", ['('] = "parenleft", [')'] = "parenright", ['*'] = "asterisk",
        ['+'] = "plus", [','] = "comma", ['-'] = "minus", ['.'] = "period", ['/'] = "slash", [':'] = "colon",
        [';'] = "semicolon", ['<'] = "less", ['='] = "equal", ['>'] = "greater", ['?'] = "question", ['@'] = "at",
        ['['] = "bracketleft", ['\\'] = "backslash", [']'] = "bracketright", ['^'] = "asciicircum", ['_'] = "underscore",
        ['`'] = "grave", ['{'] = "braceleft", ['|'] = "bar", ['}'] = "braceright", ['~'] = "asciitilde",
        ['§'] = "section", ['°'] = "degree", ['ß'] = "ssharp", ['¤'] = "currency", ['µ'] = "mu", ['¨'] = "diaeresis",
        ['´'] = "acute", ['¸'] = "cedilla", ['÷'] = "division", ['×'] = "multiply", ['€'] = "EuroSign",
        ['á'] = "aacute", ['é'] = "eacute", ['í'] = "iacute", ['ó'] = "oacute", ['ú'] = "uacute", ['ö'] = "odiaeresis",
        ['ü'] = "udiaeresis", ['ä'] = "adiaeresis", ['ë'] = "ediaeresis", ['ï'] = "idiaeresis", ['à'] = "agrave",
        ['è'] = "egrave", ['ì'] = "igrave", ['ò'] = "ograve", ['ù'] = "ugrave", ['â'] = "acircumflex", ['ê'] = "ecircumflex",
        ['î'] = "icircumflex", ['ô'] = "ocircumflex", ['û'] = "ucircumflex", ['ç'] = "ccedilla", ['ñ'] = "ntilde",
        ['ø'] = "oslash", ['å'] = "aring", ['æ'] = "ae", ['õ'] = "otilde", ['ã'] = "atilde", ['ő'] = "odoubleacute",
        ['ű'] = "udoubleacute", ['ł'] = "lstroke", ['ś'] = "sacute", ['ć'] = "cacute", ['ź'] = "zacute", ['ż'] = "zabovedot",
        ['ą'] = "aogonek", ['ę'] = "eogonek", ['ń'] = "nacute", ['č'] = "ccaron", ['š'] = "scaron", ['ž'] = "zcaron",
        ['ř'] = "rcaron", ['ě'] = "ecaron", ['ů'] = "uring", ['ý'] = "yacute", ['ť'] = "tcaron", ['ď'] = "dcaron", ['ň'] = "ncaron",
    };

    /// <summary>The Tk key name (lower case, as 1.0.8 saved it) of the character a key types: "s", "1", "exclam" for
    /// Shift+1 on a US layout, "odiaeresis" for ö. Null when there is no known name.</summary>
    public static string? KeysymForChar(char c)
    {
        if (char.IsAsciiLetterOrDigit(c)) return char.ToLowerInvariant(c).ToString();
        char lower = char.ToLowerInvariant(c);
        if (Keysyms.TryGetValue(lower, out var k)) return k.ToLowerInvariant();
        return Keysyms.TryGetValue(c, out k) ? k.ToLowerInvariant() : null;
    }

    /// <summary>Why chord ch cannot be used for action act (null = fine) (reference key_conflict; in 2.0 every
    /// shortcut can be rebound, so only the fixed keys, preset keys and the other shortcuts' keys are taken).</summary>
    public static string? KeyConflict(string ch, string act, IReadOnlyDictionary<string, string> keys, IEnumerable<string> presetKeys)
    {
        if (C.FixedKeys.Contains(ch)) return $"{ChordLabel(ch)} is used by the program (confirm, cancel, next field, close).";
        if (presetKeys.Any(k => k == ch)) return $"{ChordLabel(ch)} is the hotkey of a bitrate preset.";
        foreach (var (a, v) in keys)
            if (v == ch && a != act) return $"{ChordLabel(ch)} is already used: {KeyText[a].ToLowerInvariant()}.";
        return null;
    }

    public static readonly IReadOnlyDictionary<string, string> KeyText = new Dictionary<string, string>
    {
        ["trim_start"] = "Set the trim start at the playhead", ["trim_end"] = "Set the trim end at the playhead",
        ["speed"] = "Add a speed-up section (blue) at the playhead", ["slow"] = "Add a slow-down section (pink) at the playhead", ["pause"] = "Add a pause (orange) at the playhead",
        ["zoom"] = "Add a zoom section (white) at the playhead", ["effects"] = "Open effect settings",
        ["sound"] = "Add the picked sound at the playhead (Sound track module)",
        ["shortcuts"] = "Show this window", ["export"] = "Start the export", ["open"] = "Open a project",
        ["save"] = "Save the project", ["save_as"] = "Save the project under a new name", ["undo"] = "Undo",
        ["redo"] = "Redo", ["redo_alt"] = "Redo (second key)", ["play"] = "Play / pause (loops the trimmed part)",
        ["fullscreen"] = "Full-screen preview on / off",
        ["mute"] = "Mute / unmute the preview sound", ["select_all"] = "Select all clips",
        ["delete"] = "Remove the selected section, or else the selected clips",
        ["delete_section"] = "Remove the selected section only",
    };

    /// <summary>File-name suffix without the characters Windows forbids.</summary>
    public static string CleanSuffix(string? txt)
    {
        var kept = (txt ?? "").Where(c => !"<>:\"/\\|?*".Contains(c) && c >= 32);
        return new string(kept.ToArray()).Trim().Trim('.', '_', ' ');
    }
}
