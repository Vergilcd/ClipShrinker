using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using ClipShrinker.Core.Modules;

namespace ClipShrinker.Tests;

/// <summary>2.8: the AI editing module downloaded on demand (Core.Modules.AiModule): the manifest, install from a
/// folder or over HTTP (checked by size and SHA-256, interrupted downloads continue), update and uninstall.</summary>
public class AiModuleTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("cs_aimod_").FullName;
    string Src => Path.Combine(root, "src");
    string App => Path.Combine(root, "app");

    public AiModuleTests()
    {
        Directory.CreateDirectory(Src);
        Directory.CreateDirectory(Path.Combine(App, "modules", "ai-agent"));
        File.WriteAllText(Path.Combine(App, "modules", "ai-agent", "module.json"), "{\"id\":\"ai-agent\",\"version\":\"0.4.0\"}");
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    static string Sha(string f) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)));

    string Zip(string name, params (string File, string Text)[] entries)
    {
        string z = Path.Combine(Src, name);
        File.Delete(z);
        using var a = ZipFile.Open(z, ZipArchiveMode.Create);
        foreach (var (f, t) in entries)
            using (var w = new StreamWriter(a.CreateEntry(f).Open())) w.Write(t);
        return z;
    }

    AiPart Part(string id, string file, string kind, string dest, bool optional = false, string version = "1") =>
        new(id, id, version, file, file, new FileInfo(Path.Combine(Src, file)).Length, Sha(Path.Combine(Src, file)), kind, dest, optional, []);

    AiManifest Make(string helperText = "helper v1")
    {
        Zip("helper.zip", ("clipshrinker-mcp.exe", helperText), ("LICENSE.txt", "MIT"));
        Zip("ffmpeg.zip", ("ffmpeg.exe", "ffmpeg whisper"));
        File.WriteAllText(Path.Combine(Src, "model.bin"), new string('m', 300_000));
        var m = new AiManifest("0.4.0", "2.8.0", [
            Part("ffmpeg", "ffmpeg.zip", "zip", AiModule.FfmpegFolder),
            Part("model", "model.bin", "file", AiModule.ModelsFolder, optional: true),
            Part("helper", "helper.zip", "zip", AiModule.HelperFolder, version: helperText),
        ]);
        File.WriteAllText(Path.Combine(Src, AiModule.ManifestName), m.ToJson());
        return m;
    }

    [Fact]
    public async Task ManifestRoundTripsAndRejectsBadParts()
    {
        var m = Make();
        var back = await AiModule.FetchManifest(Src);
        Assert.Equal("0.4.0", back.ModuleVersion);
        Assert.Equal(m.Parts.Select(p => (p.Id, p.Size, p.Sha256, p.Optional)), back.Parts.Select(p => (p.Id, p.Size, p.Sha256, p.Optional)));
        Assert.Throws<InvalidDataException>(() => AiManifest.Parse("{\"parts\":[]}"));
        Assert.Throws<InvalidDataException>(() => AiManifest.Parse(m.ToJson().Replace("\"helper.zip\"", "\"..\\\\x.zip\"")));
        Assert.Throws<InvalidDataException>(() => AiManifest.Parse(m.ToJson().Replace("\"dest\": \"models\"", "\"dest\": \"..\"")));
    }

    [Fact]
    public async Task InstallsFromAFolderAndSkipsOptionalParts()
    {
        var m = Make();
        Assert.False(AiModule.IsInstalled(App));
        var need = AiModule.Missing(App, m, withOptional: false);
        Assert.Equal(["ffmpeg", "helper"], need.Select(p => p.Id).Order());
        await AiModule.Install(App, m, need, (_, _) => { }, source: Src);
        Assert.True(AiModule.IsInstalled(App));
        Assert.Equal("helper v1", File.ReadAllText(AiModule.HelperExe(App)));
        Assert.Equal(AiModule.FfmpegExe(App), AiModule.SpeechFfmpeg(App));
        Assert.False(File.Exists(Path.Combine(AiModule.ModelsDir(App), "model.bin")));
        Assert.Equal("0.4.0", AiModule.InstalledVersion(App));
        Assert.Empty(AiModule.Updates(App, m));                     // the optional model is not an update
        Assert.Equal(["model"], AiModule.Missing(App, m, withOptional: true).Select(p => p.Id));
        Assert.False(Directory.Exists(Path.Combine(AiModule.Dir(App), ".download")));
    }

    [Fact]
    public async Task ADamagedDownloadIsNotInstalled()
    {
        var m = Make();
        var bad = m.Parts.Select(p => p.Id == "helper" ? p with { Sha256 = new string('0', 64) } : p).ToList();
        var e = await Assert.ThrowsAsync<IOException>(() => AiModule.Install(App, m with { Parts = bad }, bad, (_, _) => { }, source: Src));
        Assert.Contains("SHA-256", e.Message);
        Assert.False(AiModule.IsInstalled(App));
        Assert.False(File.Exists(AiModule.HelperExe(App)));
    }

    [Fact]
    public async Task UpdateReplacesOnlyChangedPartsAndUninstallKeepsModuleJson()
    {
        var m1 = Make("helper v1");
        await AiModule.Install(App, m1, m1.Parts, (_, _) => { }, source: Src);
        Assert.True(File.Exists(Path.Combine(AiModule.ModelsDir(App), "model.bin")));
        var m2 = Make("helper v2") with { ModuleVersion = "0.5.0" };
        var ups = AiModule.Updates(App, m2);
        Assert.Equal(["helper"], ups.Select(p => p.Id));
        // like Claude Code running the helper (a running exe can be renamed, not overwritten)
        using (new FileStream(AiModule.HelperExe(App), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            await AiModule.Install(App, m2, ups, (_, _) => { }, source: Src);
        Assert.Equal("helper v2", File.ReadAllText(AiModule.HelperExe(App)));
        Assert.Equal("0.5.0", AiModule.InstalledVersion(App));
        Assert.Empty(AiModule.Updates(App, m2));

        AiModule.Uninstall(App);
        AiModule.CleanOld(App);
        Assert.False(AiModule.IsInstalled(App));
        Assert.Null(AiModule.SpeechFfmpeg(App));
        Assert.False(Directory.Exists(AiModule.ModelsDir(App)));
        Assert.True(File.Exists(Path.Combine(AiModule.Dir(App), "module.json")));
    }

    /// <summary>Serves the source folder over "HTTP"; the first answer of the model breaks off halfway.</summary>
    sealed class FakeServer(string dir) : HttpMessageHandler
    {
        public readonly List<string> Ranges = [];
        bool cut = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string name = Uri.UnescapeDataString(req.RequestUri!.Segments[^1]);
            string f = Path.Combine(dir, name);
            if (!File.Exists(f)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            byte[] all = File.ReadAllBytes(f);
            long from = req.Headers.Range?.Ranges.First().From ?? 0;
            Ranges.Add($"{name}:{from}");
            byte[] body = all[(int)from..];
            if (name == "model.bin" && cut) { cut = false; body = body[..(body.Length / 2)]; }
            var r = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            return Task.FromResult(r);
        }
    }

    [Fact]
    public async Task OverHttpABrokenDownloadContinuesWhereItStopped()
    {
        var m = Make();
        var server = new FakeServer(Src);
        const string web = "https://example.test/releases/download/ai-editing/";
        var got = await AiModule.FetchManifest(web, server);
        Assert.Equal(m.Parts.Count, got.Parts.Count);
        await Assert.ThrowsAsync<IOException>(() => AiModule.Install(App, got, got.Parts, (_, _) => { }, source: web, handler: server));
        Assert.False(AiModule.IsInstalled(App));
        await AiModule.Install(App, got, got.Parts, (_, _) => { }, source: web, handler: server);
        Assert.True(AiModule.IsInstalled(App));
        Assert.Equal(300_000, new FileInfo(Path.Combine(AiModule.ModelsDir(App), "model.bin")).Length);
        Assert.Contains("model.bin:150000", server.Ranges);           // the second try asked only for the rest
        Assert.DoesNotContain(server.Ranges, r => r.StartsWith("ffmpeg.zip") && server.Ranges.Count(x => x == r) > 1);
    }

    [Fact]
    public void ClaudeCommandPointsAtTheInstalledHelper()
    {
        string cmd = AiModule.AddCommand(@"C:\Clip Shrinker");
        Assert.Equal(@"claude mcp add clipshrinker --scope user -- ""C:\Clip Shrinker\modules\ai-agent\helper\clipshrinker-mcp.exe"" mcp " +
                     @"--app ""C:\Clip Shrinker"" --tools ""C:\Clip Shrinker\library""", cmd);
    }
}
