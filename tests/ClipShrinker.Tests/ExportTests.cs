using System.Text.Json;
using ClipShrinker.Core;
using ClipShrinker.Core.Export;
using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Tests;

/// <summary>exports.json: whole Encoder runs against the same fake encoder / file system as tools/dump_fixtures.py.</summary>
public class ExportTests
{
    static readonly JsonElement E = Fixtures.Load("exports.json");

    sealed record Run(List<string> Cmd, double Duration, string Label, double Pbase, double Pspan, bool Ok, long? Size);

    /// <summary>Port of FakeExport: each ffmpeg run "writes" (video + audio bitrate) * duration / 8 * overshoot bytes.</summary>
    sealed class Fake(double[] overshoot, bool failHw, bool failAll) : IFfmpegRunner, IExportFileSystem
    {
        public readonly Dictionary<string, long> Files = [];
        public readonly List<Run> Runs = [];

        long SizeFor(IReadOnlyList<string> cmd, double duration)
        {
            int Ix(string s) => cmd.ToList().IndexOf(s);
            long kb = long.Parse(cmd[Ix("-b:v") + 1].TrimEnd('k'));
            long aBps = 0;
            if (Ix("-b:a") >= 0)
            {
                int nMaps = cmd.Count(x => x == "-map") - 1;
                aBps = long.Parse(cmd[Ix("-b:a") + 1].TrimEnd('k')) * 1000 * nMaps;
            }
            int k = Runs.Count(r => r.Ok);
            double f = overshoot[Math.Min(k, overshoot.Length - 1)];
            return Int((kb * 1000 + aBps) * duration / 8 * f);
        }

        public (bool Ok, string Log) Run(IReadOnlyList<string> cmd, double duration, string label, double pbase, double pspan)
        {
            bool ok = !failAll && !(failHw && cmd.Contains("-hwaccel"));
            long? size = null;
            if (ok) { size = SizeFor(cmd, duration); Files[cmd[^1]] = size.Value; }
            Runs.Add(new Run(cmd.ToList(), duration, label, pbase, pspan, ok, size));
            return (ok, ok ? "" : "simulated ffmpeg error");
        }

        public void Kill() { }
        public long GetSize(string path) => Files.TryGetValue(path, out var s) ? s : throw new FileNotFoundException(path);
        public void Replace(string a, string b)
        {
            if (!Files.Remove(a, out var s)) throw new FileNotFoundException(a);
            Files[b] = s;
        }
        public void Remove(string path)
        {
            if (!Files.Remove(path)) throw new FileNotFoundException(path);
        }
    }

    static ExportSettings Settings(JsonElement st) => new()
    {
        TargetMb = st.GetProperty("target_mb").NumOrNull(),
        BitrateMbps = st.GetProperty("bitrate_mbps").NumOrNull(),
        ResMode = st.GetProperty("res_mode").GetString()!,
        Codec = st.GetProperty("codec").GetString()!,
        AudioK = st.GetProperty("audio_k").GetString()!,
        Ratio = st.GetProperty("ratio").NumOrNull(),
        Xfade = st.GetProperty("xfade").Str(),
        T = st.GetProperty("T").Num(),
        Tonemap = st.GetProperty("tonemap").GetBoolean(),
        FastAudio = st.GetProperty("fast_audio").GetString()!,
        SlowAudio = st.GetProperty("slow_audio").GetString()!,
        SlowPitch = st.GetProperty("slow_pitch").GetString()!,
        FastPitch = st.GetProperty("fast_pitch").GetString()!,
        Vhs = st.GetProperty("vhs").GetBoolean(),
        SlowInterp = st.GetProperty("slow_interp").GetString()!,
        FastVol = st.GetProperty("fast_vol").Int(),
        SlowVol = st.GetProperty("slow_vol").Int(),
        ZoomFx = st.GetProperty("zoom_fx").GetString()!,
        ZoomSnd = st.GetProperty("zoom_snd").GetString()!,
        ZoomLen = st.GetProperty("zoom_len").GetString()!,
        ZoomVol = st.GetProperty("zoom_vol").Int(),
        MixAudio = st.GetProperty("mix_audio").GetBoolean(),
    };

    [Fact]
    public void EncoderRuns()
    {
        var m = new Mismatches("Encoder.encode");
        foreach (var x in E.EnumerateArray())
        {
            string name = x.GetProperty("name").GetString()!;
            m.Run(name, () =>
            {
                var fk = x.GetProperty("fake");
                var fake = new Fake(fk.Opt("overshoot")?.EnumerateArray().Select(v => v.Num()).ToArray() ?? [1.0],
                                    fk.Opt("fail_hw")?.GetBoolean() ?? false, fk.Opt("fail_all")?.GetBoolean() ?? false);
                var status = new List<string>();
                var progress = new List<double>();
                var enc = new Encoder(fake, fake, progress.Add, status.Add);
                var clips = x.GetProperty("clips").EnumerateArray().Select(c => c.GetProperty("clip").Clip()).ToList();
                var st = Settings(x.GetProperty("st"));
                string? result = null, error = null;
                try { result = I(enc.Encode(x.GetProperty("out").GetString()!, clips, st)); }
                catch (ExportException ex) { error = "RuntimeError: " + ex.Message; }

                m.Equal(x.Opt("result") is { } r ? I((long)r.Num()) : null, result, name + ": result");
                m.Equal(x.Opt("error")?.GetString(), error, name + ": error");
                var runs = x.GetProperty("runs").EnumerateArray().ToList();
                m.Equal(runs.Count, fake.Runs.Count, name + ": number of ffmpeg runs");
                for (int i = 0; i < Math.Min(runs.Count, fake.Runs.Count); i++)
                {
                    var w = runs[i];
                    var g = fake.Runs[i];
                    m.Seq(w.GetProperty("cmd").Strs(), g.Cmd, $"{name}: run {i} command");
                    m.Equal(w.GetProperty("duration").Num(), g.Duration, $"{name}: run {i} duration");
                    m.Equal(w.GetProperty("label").GetString(), g.Label, $"{name}: run {i} label");
                    m.Equal(w.GetProperty("pbase").Num(), g.Pbase, $"{name}: run {i} pbase");
                    m.Equal(w.GetProperty("pspan").Num(), g.Pspan, $"{name}: run {i} pspan");
                    m.Equal(w.GetProperty("ok").GetBoolean(), g.Ok, $"{name}: run {i} ok");
                    m.Equal(w.Opt("size") is { } s ? (long?)s.Num() : null, g.Size, $"{name}: run {i} size");
                }
                m.Seq(x.GetProperty("status").Strs(), status, name + ": status texts");
                m.Equal(string.Join(" ", x.GetProperty("progress").EnumerateArray().Select(v => v.Num().ToString("R"))),
                        string.Join(" ", progress.Select(v => v.ToString("R"))), name + ": progress");
                m.Equal(string.Join(" ", x.GetProperty("files_left").EnumerateObject().Select(p => $"{p.Name}={p.Value.GetRawText()}").Order()),
                        string.Join(" ", fake.Files.Select(p => $"{p.Key}={p.Value}").Order()), name + ": files left");
                var after = x.GetProperty("globals_after").Effects() with { ZoomPlay = [] };
                m.Equal(after.ToString(), (EffectSettings.FromExport(st) with { ZoomPlay = [] }).ToString(), name + ": effect settings");
            });
        }
        m.AssertNone();
    }
}
