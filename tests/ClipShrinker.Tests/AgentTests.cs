using System.Text.Json.Nodes;
using ClipShrinker.Core;
using ClipShrinker.Core.Agent;
using ClipShrinker.Core.Media;
using ClipShrinker.Core.Project;

namespace ClipShrinker.Tests;

/// <summary>Dev (AI editing module): the agent tools and the pipe between the MCP helper and the app.</summary>
[Collection("AgentPipe")]
public class AgentTests
{
    sealed class FakeHost : IAgentHost
    {
        public List<TimelineClip> Rows = [new("C:/v/a.mp4", new MediaInfo(1920, 1080, 60, 30, 1, 8e6, 0, null, null, null))];
        public List<string> Edits = [];
        public Tools Tools { get; } = new(null, null);
        public bool Live => true;
        public string? ProjectPath => null;
        public IReadOnlyList<TimelineClip> Clips => Rows.ToList();
        public AppState State { get; } = AppState.FromSettings(null);
        public void Edit(string label, Action change) { Edits.Add(label); change(); }
        public void RemoveClip(TimelineClip clip) { Edits.Add("remove"); Rows.Remove(clip); }
        public string OpenProject(string path) => "";
        public string AddVideos(IReadOnlyList<string> paths) => "";
        public (string Text, bool Ok) Export(string? output, string? mode) => ("exported", true);
        public IReadOnlyList<string> Codecs => ["HEVC (hevc_nvenc)", "H.264 (h264_nvenc)"];
        public List<string> SettingChanges = [];
        public void Settings(string label, Action change) { SettingChanges.Add(label); change(); }
        public string? ExportFolder { get; set; } = "";
        public string AppDir { get; set; } = Path.GetTempPath();
        public bool? ModuleOn(string id) => true;
    }

    // ------------------------------------------------------------------ 2.8
    [Fact]
    public void ExportSettingsAreMatchedAndAppliedTogether()
    {
        var h = new FakeHost();
        var r = AgentTools.Call(h, "set_export_settings", new JsonObject
        {
            ["codec"] = "h264", ["size"] = 25, ["crop"] = "4:3", ["prefix"] = "{date}", ["suffix"] = "ranked", ["resolution"] = "1080p",
        });
        Assert.False(r["isError"]!.GetValue<bool>(), TextOf(r));
        Assert.Equal(("H.264 (h264_nvenc)", "25", "1080p"), (h.State.Codec, h.State.Size, h.State.Res));
        Assert.StartsWith("4:3", h.State.Crop);
        Assert.Contains($"{DateTime.Now:yyyy-MM-dd}_a_ranked.mp4", TextOf(r));
        Assert.Equal(["export settings"], h.SettingChanges);                     // one change, not an undo step

        // a wrong value refuses the whole call: the good ones next to it are not applied either
        r = AgentTools.Call(h, "set_export_settings", new JsonObject { ["size"] = 99, ["codec"] = "AV1" });
        Assert.True(r["isError"]!.GetValue<bool>());
        Assert.Contains("is not one of", TextOf(r));
        Assert.Equal("25", h.State.Size);

        // with a fixed bitrate the resolution comes from the bitrate list
        AgentTools.Call(h, "set_export_settings", new JsonObject { ["bitrate"] = "20", ["resolution"] = "original" });
        Assert.Equal(("20", "Original"), (h.State.Bitrate, h.State.Res));
        Assert.Contains("ignored: a fixed bitrate", TextOf(AgentTools.Call(h, "export_settings", [])));
    }

    [Fact]
    public void PictureToolSetsWhiteBalanceAndSaysWhatItWas()
    {
        var h = new FakeHost();
        var r = AgentTools.Call(h, "set_picture", new JsonObject { ["temperature"] = 20, ["tint"] = -10, ["denoise"] = 30 });
        Assert.Equal((20, -10, 30, 0), (h.State.PicTemp, h.State.PicTint, h.State.PicDenoise, h.State.PicBright));
        Assert.Contains("before: brightness 0, contrast 0, saturation 0, temperature 0, tint 0, denoise 0", TextOf(r));
        Assert.Empty(h.Edits);                                                   // a setting, not a clip edit
    }

    [Fact]
    public void SoundsArePlacedFromTheLibrary()
    {
        var h = new FakeHost { AppDir = Path.Combine(Path.GetTempPath(), "cs_agent_sounds_" + Guid.NewGuid().ToString("N")) };
        try
        {
            var r = AgentTools.Call(h, "add_sound", new JsonObject { ["clip"] = 1, ["sound"] = "boom" });
            Assert.True(r["isError"]!.GetValue<bool>());
            Assert.Contains("No sound \"boom\"", TextOf(r));
            r = AgentTools.Call(h, "sound_library", []);
            Assert.Contains("is empty", TextOf(r));
            r = AgentTools.Call(h, "remove_sounds", new JsonObject { ["clip"] = 1 });
            Assert.Contains("has no sounds", TextOf(r));
        }
        finally { if (Directory.Exists(h.AppDir)) Directory.Delete(h.AppDir, true); }
    }

    [Fact]
    public void DownloadRefusesPlainHttp()
    {
        var r = AgentTools.Call(new FakeHost(), "download_sound", new JsonObject { ["url"] = "http://example.com/a.mp3" });
        Assert.True(r["isError"]!.GetValue<bool>());
        Assert.Contains("https", TextOf(r));
    }

    [Fact]
    public void NewToolsAreListed()
    {
        var names = AgentTools.List().Select(t => t!["name"]!.GetValue<string>()).ToList();
        foreach (var n in new[] { "export_settings", "set_export_settings", "sound_library", "download_sound", "add_sound", "remove_sounds", "transcribe" })
            Assert.Contains(n, names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    static string TextOf(JsonObject r) => string.Join("\n", r["content"]!.AsArray().Select(c => c!["text"]?.GetValue<string>()));

    [Fact]
    public void EditsGoThroughTheHost()
    {
        var h = new FakeHost();
        var r = AgentTools.Call(h, "set_trim", new JsonObject { ["clip"] = 1, ["start"] = 5, ["end"] = 20 });
        Assert.False(r["isError"]!.GetValue<bool>());
        Assert.Equal((5.0, 20.0), (h.Rows[0].Start, h.Rows[0].End));
        Assert.Contains("shows on the timeline", TextOf(r));
        r = AgentTools.Call(h, "add_section", new JsonObject { ["clip"] = 1, ["kind"] = "speed", ["from"] = 6, ["to"] = 10, ["speed"] = 4 });
        Assert.Single(h.Rows[0].Sections);
        r = AgentTools.Call(h, "add_section", new JsonObject { ["clip"] = 1, ["kind"] = "speed", ["from"] = 8, ["to"] = 12, ["speed"] = 2 });
        Assert.True(r["isError"]!.GetValue<bool>());                             // overlaps: refused, nothing changed
        Assert.Equal(["trim", "speed section"], h.Edits);
        r = AgentTools.Call(h, "set_trim", new JsonObject { ["clip"] = 1, ["start"] = 12, ["end"] = 20 });
        Assert.Empty(h.Rows[0].Sections);                                        // the section outside the trim went
        r = AgentTools.Call(h, "clip_info", new JsonObject { ["clip"] = 2 });
        Assert.Contains("There is no clip 2", TextOf(r));
        AgentTools.Call(h, "remove_clip", new JsonObject { ["clip"] = 1 });
        Assert.Empty(h.Rows);
    }

    [Fact]
    public void NumbersUseADot()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");
        try
        {
            var h = new FakeHost();
            AgentTools.Call(h, "set_trim", new JsonObject { ["clip"] = 1, ["start"] = 1.5, ["end"] = 20 });
            Assert.Contains("kept 1.5-20 s", TextOf(AgentTools.Call(h, "clip_list", [])));
            Assert.Equal("hu-HU", System.Globalization.CultureInfo.CurrentCulture.Name);   // put back afterwards
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void PipeCarriesCallsAndCountsConnections()
    {
        var h = new FakeHost();
        using var server = new AgentPipeServer(() => h);
        var counts = new List<int>();
        server.ConnectionsChanged += n => { lock (counts) counts.Add(n); };
        server.Start();
        var client = new AgentPipeClient();
        JsonObject? r = null;
        for (int i = 0; i < 20 && r is null; i++) { r = client.TryCall("set_trim", new JsonObject { ["clip"] = 1, ["start"] = 2, ["end"] = 9 }); if (r is null) Thread.Sleep(100); }
        Assert.NotNull(r);
        Assert.Equal((2.0, 9.0), (h.Rows[0].Start, h.Rows[0].End));            // done by the "app" side
        Assert.True(client.Connected);
        Assert.Equal(1, server.Connections);
        client.Dispose();
        for (int i = 0; i < 20 && server.Connections > 0; i++) Thread.Sleep(100);
        Assert.Equal(0, server.Connections);                                     // the header turns orange again
        lock (counts) Assert.Equal([1, 0], counts);
    }
}
