using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;
using ClipShrinker.Core.Plugins;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Settings;

namespace ClipShrinker.Tests;

/// <summary>Tests that switch the global plugin flags run alone, so the fixture tests never see a plugin off.</summary>
[CollectionDefinition("PluginFlags", DisableParallelization = true)]
public class PluginFlagsCollection;

/// <summary>2.4: plugin manifests, the Picture adjustments filter, and what switching a plugin off changes.</summary>
[Collection("PluginFlags")]
public class PluginTests
{
    static TimelineClip Clip(string path = "C:/v/a.mp4", double dur = 30) =>
        new(path, new MediaInfo(1920, 1080, 60, dur, 1, 8e6, 0, null, null, null));

    [Fact]
    public void ManifestReadsAllFields()
    {
        var m = PluginManifest.Parse("""{"id":"x","name":"X plug","author":"Me","version":"1.2.3","builtin":true,"description":"d"}""", "folder")!;
        Assert.Equal(("x", "X plug", "Me", "1.2.3", "d", true), (m.Id, m.Name, m.Author, m.Version, m.Description, m.Builtin));
    }

    [Fact]
    public void ManifestFallsBackToFolderName()
    {
        var m = PluginManifest.Parse("{}", "my-plugin")!;
        Assert.Equal(("my-plugin", "my-plugin", "unknown", "?", false), (m.Id, m.Name, m.Author, m.Version, m.Builtin));
        Assert.Null(PluginManifest.Parse("not json", "x"));
        Assert.Null(PluginManifest.Parse("[1]", "x"));
    }

    [Fact]
    public void ScanFindsFoldersWithManifests()
    {
        string root = Directory.CreateTempSubdirectory("cs_plugins_").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "b"));
            File.WriteAllText(Path.Combine(root, "b", "plugin.json"), """{"name":"Beta","version":"2"}""");
            File.WriteAllText(Path.Combine(root, "b", "CHANGELOG.txt"), "  2: fixed  \n");
            Directory.CreateDirectory(Path.Combine(root, "a"));
            File.WriteAllText(Path.Combine(root, "a", "plugin.json"), """{"name":"Alpha"}""");
            Directory.CreateDirectory(Path.Combine(root, "empty"));      // no manifest: not a plugin
            var list = PluginManifest.Scan(root);
            Assert.Equal(["Alpha", "Beta"], list.Select(x => x.Manifest.Name));
            Assert.Equal("2: fixed", PluginManifest.ReadChangelog(list[1].Dir));
            Assert.Null(PluginManifest.ReadChangelog(list[0].Dir));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("1.0", "1.0.1", -1)]
    [InlineData("2.0.0", "10.0.0", -1)]
    public void VersionsCompareByNumber(string a, string b, int want) => Assert.Equal(want, PluginManifest.CompareVersions(a, b));

    [Fact]
    public void ShippedManifestsAreValid()
    {
        string root = Path.Combine(FindRepo(), "plugins");
        foreach (var id in PluginIds.Builtin)
        {
            var m = PluginManifest.Read(Path.Combine(root, id));
            Assert.NotNull(m);
            Assert.Equal(id, m!.Id);
            Assert.True(m.Builtin);
            Assert.NotNull(PluginManifest.ReadChangelog(Path.Combine(root, id)));
        }
    }

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "plugins")) && File.Exists(Path.Combine(d.FullName, "ClipShrinker.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("repository root");
    }

    // ------------------------------------------------------------------ Picture adjustments
    [Fact]
    public void NeutralPictureHasNoFilter()
    {
        Assert.Null(PictureAdjust.Filter(0, 0, 0));
    }

    [Fact]
    public void PictureFilterStrings()
    {
        Assert.Equal("lutyuv=y='val':u='clip((val-(minval+maxval)/2)*0.000+(minval+maxval)/2,minval,maxval)':v='clip((val-(minval+maxval)/2)*0.000+(minval+maxval)/2,minval,maxval)'",
                     PictureAdjust.Filter(0, 0, -100));
        Assert.Equal("lutyuv=y='clip((val-(minval+maxval)/2)*1.200+(minval+maxval)/2+0.150*(maxval-minval),minval,maxval)':u='val':v='val'",
                     PictureAdjust.Filter(50, 20, 0));
        Assert.Equal(PictureAdjust.Filter(0, 0, 100), PictureAdjust.Filter(0, 0, 500));   // clamped to +100 %
    }

    [Theory]
    [InlineData("25", 25)]
    [InlineData("+25%", 25)]
    [InlineData(" -40 % ", -40)]
    [InlineData("12.6", 13)]
    [InlineData("12,4%", 12)]
    [InlineData("250", 100)]
    [InlineData("-1e9", -100)]
    [InlineData("", 0)]
    [InlineData("%", 0)]
    public void TypedPercents(string text, int want) => Assert.Equal(want, PictureAdjust.ParsePercent(text));

    [Theory]
    [InlineData("abc")]
    [InlineData("--5")]
    [InlineData("NaN")]
    public void TypedNonsenseIsRejected(string text) => Assert.Null(PictureAdjust.ParsePercent(text));

    [Fact]
    public void PercentsAreShownWithSign()
    {
        Assert.Equal(("+25%", "-40%", "0%"), (PictureAdjust.FormatPercent(25), PictureAdjust.FormatPercent(-40), PictureAdjust.FormatPercent(0)));
    }

    [Fact]
    public void PictureOnlyWhenPluginOn()
    {
        var s = AppState.FromSettings(null);
        s.PicBright = 10;
        Assert.Null(s.PictureFilter());
        s.PictureOn = true;
        Assert.NotNull(s.PictureFilter());
        var clips = ExportPlanner.PrepareClips(s, [Clip()]);
        Assert.Equal([s.PictureFilter()!], clips[0].PluginVf!);
        var vf = Filters.ClipVfilters(clips[0], 1920, 1080, null, false);
        Assert.Equal(s.PictureFilter(), vf[^1]);                        // after scale / pad / setsar
        s.PictureOn = false;
        Assert.Null(ExportPlanner.PrepareClips(s, [Clip()])[0].PluginVf);
    }

    [Fact]
    public void PictureSettingsRoundTrip()
    {
        var s = AppState.FromSettings(null);
        (s.PicBright, s.PicContrast, s.PicSat) = (-20, 35, 100);
        var d = new JsonObject();
        s.WriteTo(d);
        var back = AppState.FromSettings(d);
        Assert.Equal((-20, 35, 100), (back.PicBright, back.PicContrast, back.PicSat));
        var bad = AppState.FromSettings(new JsonObject { ["pic_bright"] = 999, ["pic_sat"] = "x" });
        Assert.Equal((100, 0), (bad.PicBright, bad.PicSat));
    }

    [Fact]
    public void ProjectsKeepPictureOnlyWhenChanged()
    {
        var s = AppState.FromSettings(null);
        var st = (JsonObject)ProjectSave.Data([Clip()], 0, s, null)["settings"]!;
        Assert.False(st.ContainsKey("pic_bright"));                    // unchanged: the file is as before
        s.PicSat = -40;
        st = (JsonObject)ProjectSave.Data([Clip()], 0, s, null)["settings"]!;
        Assert.Equal(-40, (int)st["pic_sat"]!);
        var other = AppState.FromSettings(null);
        other.PicBright = 30;
        other.ApplyProject(st);
        Assert.Equal((0, -40), (other.PicBright, other.PicSat));       // the project's picture replaces the current one
    }

    // ------------------------------------------------------------------ plugins switched off
    [Fact]
    public void TimelineEffectsOffDropsSectionsTransitionsAndKeys()
    {
        var row = Clip();
        row.Sections.Add(new SpeedSection(2, 6, 2.0));
        row.Zooms.Add(new ZoomSection(10, 14, 0.25, 0.25, 0.5));
        var s = AppState.FromSettings(null);
        try
        {
            Features.TimelineEffects = false;
            var c = row.ToExport();
            Assert.Null(c.Pieces);
            Assert.Empty(c.Zooms);
            Assert.Single(row.Sections);                               // kept for when the plugin comes back
            Assert.Null(s.ToExportSettings(true, out _)!.Xfade);
            var ed = new TimelineEditor(row);
            Assert.Empty(ed.Vis());
            Assert.Empty(ed.VisZ());
            Assert.Null(ed.AddSection());
            Assert.Null(ed.AddZoom());
            Assert.False(ed.RemoveSection(row.Sections[0]));
            Assert.Single(row.Sections);
        }
        finally { Features.TimelineEffects = true; }
        Assert.NotNull(row.ToExport().Pieces);
        Assert.Equal(C.TransitionMode(s.Trans), s.ToExportSettings(true, out _)!.Xfade);
    }

    [Fact]
    public void BitratePresetsOffExportsWithTheExportPanelBitrate()
    {
        var row = Clip();
        row.Preset = "Discord";
        var preset = new Preset("Discord", 6, "");
        var s = AppState.FromSettings(null);
        var st = s.ToExportSettings(false, out _)!;
        var clips = ExportPlanner.PrepareClips(s, [row]);
        try
        {
            Features.BitratePresets = false;
            var job = ExportPlanner.OneFile(s, st, [row], clips, "C:/out/a.mp4", false, _ => preset);
            Assert.False(job.HasOverride);
            Assert.Null(job.Settings.BitrateMbps);
            Assert.Equal("Discord", row.Preset);                       // kept
        }
        finally { Features.BitratePresets = true; }
        var on = ExportPlanner.OneFile(s, st, [row], clips, "C:/out/a.mp4", false, _ => preset);
        Assert.Equal(6, on.Settings.BitrateMbps);
    }
}
