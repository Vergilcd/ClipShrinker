namespace ClipShrinker.Core;

/// <summary>Constants and option lists of the 1.0.8 reference (same names, same order).</summary>
public static class C
{
    public const string AppName = "ClipShrinker";
    public const string AppVersion = "2.8.0-beta1";
    public const string AppId = "ClipShrinker.Desktop.App";
    public const string ProjectExt = ".clipproj";
    public const string RepoUrl = "https://github.com/Vergilcd/ClipShrinker";

    /// <summary>name -> width/height ratio (null = keep as is).</summary>
    public static readonly IReadOnlyList<(string Name, double? Ratio)> Aspects =
    [
        ("Original (no crop)", null), ("21:9", 21.0 / 9), ("16:9", 16.0 / 9), ("16:10", 16.0 / 10), ("4:3", 4.0 / 3),
    ];

    /// <summary>name -> ffmpeg xfade transition (null = hard cut, "glitch" = own filter chain).</summary>
    public static readonly IReadOnlyList<(string Name, string? Mode)> Transitions =
    [
        ("Hard cut", null), ("Crossfade (gradient)", "fade"), ("Fade through black", "fadeblack"),
        ("Fade through white", "fadewhite"), ("Glitch (digital)", "glitch"),
        ("Gradient wipe, left to right", "smoothright"), ("Gradient wipe, right to left", "smoothleft"),
        ("Gradient wipe, upwards", "smoothup"), ("Gradient wipe, downwards", "smoothdown"),
        ("Swipe left (push)", "slideleft"), ("Swipe right (push)", "slideright"), ("Swipe up (push)", "slideup"),
        ("Swipe down (push)", "slidedown"), ("Circle open", "circleopen"), ("Circle close", "circleclose"),
        ("Circle crop", "circlecrop"), ("Clock wipe (radial)", "radial"), ("Diagonal wipe", "diagtl"),
        ("Zoom in", "zoomin"), ("Pixelize", "pixelize"), ("Blur (horizontal)", "hblur"), ("Distance", "distance"),
    ];

    public static string? TransitionMode(string name)
    {
        foreach (var (n, mode) in Transitions) if (n == name) return mode;
        return null;
    }

    public const string Glitch = "glitch";
    /// <summary>Every codec label (2.6: all encoders; the app lists the ones that work on this PC).</summary>
    public static readonly string[] Codecs = [.. Media.Encoders.Labels];
    /// <summary>2.6: 75% gave way to fixed heights (4K, 1440p, 1080p, 720p; never upscaled). A saved "75%" falls back to
    /// the default.</summary>
    public static readonly string[] ResSizeMode = ["Auto", "100%", "4K", "1440p", "1080p", "720p", "50%"];
    public static readonly string[] ResBitrateMode = ["Original", "4K", "1440p", "1080p", "720p", "50%"];
    public static readonly double[] TickSteps = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200];
    public const double MinView = 1.0;
    public const int MaxThumbCache = 800;
    public const int ThumbBatch = 12;

    public const int MaxAudio = 4;
    public const string Mix = "amix=inputs={n}:duration=longest:normalize=0,alimiter=limit=0.97:level=0";
    public static string MixFilter(int n) => Mix.Replace("{n}", Py.Py.I(n));
    public const double SpeedMin = 0.1, SpeedMax = 100.0;
    public const double MinSection = 0.2;
    public const double VhsMin = 0.15;
    public const double ZoomMinW = 0.1;

    public static readonly string[] FastAudio = ["VHS effect with sound", "Normal sped-up sound", "Mute"];
    public static readonly string[] SlowAudio = ["Slow-mo effect with sound", "Normal slow-mo sound", "Mute"];
    public static readonly string[] SlowPitch = ["Slows down with the video (deep)", "Stays the same (stretched)"];
    public static readonly string[] FastPitch = ["Speeds up with the video (high)", "Stays the same (compressed)"];
    public static readonly string[] SlowInterp = ["Repeat frames", "Frame blending", "RIFE AI (smoothest, needs a GPU)"];
    public static readonly string[] ZoomFx = ["Zoom motion", "No effect (instant)"];
    public static readonly string[] ZoomAudio = ["Whoosh sound", "Mute"];

    public const int PvW = 240, PvH = 136, PvFps = 15;          // transition hover preview
    public const int MaxPvW = 1600, MaxPvH = 900, PlayFpsCap = 30;
    public const int FullPvW = 1920, FullPvH = 1080;           // the full-screen preview

    /// <summary>Every keyboard shortcut and its default key. 1.0.8 could rebind the first six; 2.0 all of them.</summary>
    public static readonly IReadOnlyDictionary<string, string> KeyDefaults = new Dictionary<string, string>
    {
        ["trim_start"] = "1", ["trim_end"] = "2", ["speed"] = "s", ["slow"] = "d", ["zoom"] = "z", ["effects"] = "f2",
        ["shortcuts"] = "f1", ["export"] = "ctrl+e", ["open"] = "ctrl+o", ["save"] = "ctrl+s", ["save_as"] = "ctrl+shift+s",
        ["undo"] = "ctrl+z", ["redo"] = "ctrl+shift+z", ["redo_alt"] = "ctrl+y", ["play"] = "space", ["mute"] = "m",
        ["select_all"] = "ctrl+a", ["delete"] = "delete", ["delete_section"] = "backspace", ["fullscreen"] = "f",
        ["sound"] = "a",                                       // (2.7 Sound track module)
    };

    /// <summary>Shortcuts added after 1.0.8, with a second key for a settings file whose shortcuts already use the default.</summary>
    public static readonly IReadOnlyDictionary<string, string> KeyFallbacks = new Dictionary<string, string> { ["fullscreen"] = "f11", ["sound"] = "shift+a" };

    /// <summary>Keys that keep their own job and cannot be given to a shortcut: confirm, cancel, next field,
    /// close window.</summary>
    public static readonly IReadOnlySet<string> FixedKeys = new HashSet<string> { "escape", "return", "kp_enter", "tab", "alt+f4" };

    /// <summary>Shortcuts that also work while a text field has the focus (when their key is not one the field needs).</summary>
    public static readonly IReadOnlySet<string> KeysWhileTyping = new HashSet<string> { "shortcuts", "effects", "open", "save", "save_as" };

    /// <summary>Keys a text field uses itself (select all, copy, paste, cut, undo, redo).</summary>
    public static readonly IReadOnlySet<string> TextFieldChords = new HashSet<string>
        { "ctrl+a", "ctrl+c", "ctrl+v", "ctrl+x", "ctrl+z", "ctrl+y", "ctrl+shift+z", "shift+delete", "shift+insert", "ctrl+insert" };

    public static readonly IReadOnlySet<string> ReservedKeys = new HashSet<string>
        { "s", "d", "m", "1", "2", "kp_1", "kp_2", "space", "return", "kp_enter", "escape", "tab", "delete", "backspace", "f1", "f2" };
    public static readonly IReadOnlySet<string> ReservedPlain = new HashSet<string>
        { "space", "f1", "delete", "backspace", "escape", "return", "tab", "m" };
    public static readonly IReadOnlySet<string> ReservedCtrl = new HashSet<string>
        { "ctrl+o", "ctrl+s", "ctrl+shift+s", "ctrl+z", "ctrl+y", "ctrl+shift+z", "ctrl+a", "ctrl+e", "ctrl+shift+e" };

    public static readonly IReadOnlyDictionary<string, string> KeyShown = new Dictionary<string, string>
    {
        ["exclam"] = "!", ["at"] = "@", ["numbersign"] = "#", ["dollar"] = "$", ["percent"] = "%", ["asciicircum"] = "^",
        ["ampersand"] = "&", ["asterisk"] = "*", ["parenleft"] = "(", ["parenright"] = ")", ["minus"] = "-", ["plus"] = "+",
        ["equal"] = "=", ["comma"] = ",", ["period"] = ".", ["slash"] = "/", ["backslash"] = "\\", ["semicolon"] = ";",
        ["apostrophe"] = "'", ["bracketleft"] = "[", ["bracketright"] = "]", ["grave"] = "`", ["space"] = "Space",
        ["return"] = "Enter", ["tab"] = "Tab", ["prior"] = "Page Up", ["next"] = "Page Down", ["escape"] = "Esc",
    };
}
