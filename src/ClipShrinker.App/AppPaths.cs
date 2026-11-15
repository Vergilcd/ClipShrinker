using System.IO;
using ClipShrinker.Core.Export;
using ClipShrinker.Core.Media;

namespace ClipShrinker.App;

/// <summary>Where the app finds its files. The layout matches 1.0.8: ClipShrinker.exe next to a "library" folder
/// that holds ffmpeg, ffprobe, rife\ and settings.json.</summary>
public static class AppPaths
{
    public static string ExeDir { get; } = AppContext.BaseDirectory.TrimEnd('\\', '/');
    public static string LibraryDir { get; } = Path.Combine(ExeDir, "library");
    public static string SettingsFile => Path.Combine(LibraryDir, "settings.json");
    public static string PresetsFile => Path.Combine(LibraryDir, "presets.json");

    /// <summary>Debug builds also look for the 1.0.8 "Dev" folder above the build output, so ffmpeg, RIFE and the
    /// text files are found without copying them. Settings are still written next to the debug exe.</summary>
    static readonly Lazy<string?> DevRoot = new(() =>
    {
#if DEBUG
        for (var d = new DirectoryInfo(ExeDir); d is not null; d = d.Parent)
        {
            var dev = Path.Combine(d.FullName, "Dev");
            if (File.Exists(Path.Combine(dev, "library", "ffmpeg.exe"))) return dev;
        }
#endif
        return null;
    });

    static string?[] ToolDirs => [LibraryDir, ExeDir, DevRoot.Value is { } r ? Path.Combine(r, "library") : null];

    public static Tools LocateTools() => Tools.Locate(ToolDirs);

    /// <summary>plugins\ next to the exe: one folder per plugin with plugin.json and CHANGELOG.txt.</summary>
    public static string PluginsDir => Path.Combine(ExeDir, "plugins");
    public static string PluginDir(string id) => Path.Combine(PluginsDir, id);

    /// <summary>Since 2.4 RIFE belongs to the Timeline effects plugin: plugins\timeline-effects\rife.</summary>
    public static string RifeDir => Path.Combine(PluginDir(Core.Plugins.PluginIds.TimelineEffects), "rife");

    /// <summary>The plugin's rife\ folder; older layouts (rife\ next to ffmpeg, e.g. the Dev folder in Debug) still work.</summary>
    public static RifeInstall? FindRife(Tools t) =>
        RifeInstall.Find(RifeDir) ?? (t.Ffmpeg is { } f ? RifeInstall.Find(Path.Combine(Path.GetDirectoryName(f)!, "rife")) : null);

    /// <summary>2.3 and older kept RIFE in library\rife: it moves into the Timeline effects plugin folder (once).</summary>
    public static void MoveOldRife()
    {
        string old = Path.Combine(LibraryDir, "rife");
        try
        {
            if (Directory.Exists(old) && !Directory.Exists(RifeDir) && Directory.Exists(Path.GetDirectoryName(RifeDir)))
                Directory.Move(old, RifeDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Debug builds: the 2.0 text files in CSharp\dist (the release script copies them next to the exe).</summary>
    static readonly Lazy<string?> DistDocs = new(() =>
    {
#if DEBUG
        for (var d = new DirectoryInfo(ExeDir); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "dist", "CHANGELOG.txt"))) return Path.Combine(d.FullName, "dist");
#endif
        return null;
    });

    /// <summary>Folder with CHANGELOG.txt, LICENSE.txt, README.txt (the exe folder; in Debug CSharp\dist).</summary>
    public static string DocsDir => File.Exists(Path.Combine(ExeDir, "CHANGELOG.txt")) ? ExeDir : DistDocs.Value ?? DevRoot.Value ?? ExeDir;

    /// <summary>RIFE's LICENSE.txt: in the plugin folder, else an older library\rife (in Debug the Dev folder's).</summary>
    public static string RifeLicense
    {
        get
        {
            string p = Path.Combine(RifeDir, "LICENSE.txt");
            if (File.Exists(p)) return p;
            string lib = Directory.Exists(Path.Combine(LibraryDir, "rife")) || DevRoot.Value is null ? LibraryDir : Path.Combine(DevRoot.Value, "library");
            return Path.Combine(lib, "rife", "LICENSE.txt");
        }
    }
}
