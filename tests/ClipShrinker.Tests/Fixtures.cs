using System.Text.Json;
using ClipShrinker.Core;
using ClipShrinker.Core.Ffmpeg;

namespace ClipShrinker.Tests;

/// <summary>Loads tests/fixtures/*.json (made by tools/dump_fixtures.py from Python 1.0.8) and turns JSON into Core types.</summary>
public static class Fixtures
{
    static readonly Lazy<string> Dir = new(() =>
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures");
            if (File.Exists(Path.Combine(p, "meta.json"))) return p;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures not found above " + AppContext.BaseDirectory);
    });

    public static string FixtureDir => Dir.Value;
    public static string FilesDir => Path.Combine(Dir.Value, "files");

    public static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir.Value, name))).RootElement;

    public static bool IsNull(this JsonElement e) => e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    public static JsonElement? Opt(this JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && !v.IsNull() ? v : null;

    /// <summary>A number, or NaN / inf written as {"float": "nan"}.</summary>
    public static double Num(this JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
            return e.GetProperty("float").GetString() switch
            {
                "nan" => double.NaN, "inf" => double.PositiveInfinity, "-inf" => double.NegativeInfinity,
                var s => throw new FormatException(s),
            };
        if (e.ValueKind == JsonValueKind.True) return 1;
        if (e.ValueKind == JsonValueKind.False) return 0;
        return e.GetDouble();
    }

    public static double? NumOrNull(this JsonElement e) => e.IsNull() ? null : e.Num();
    public static int Int(this JsonElement e) => e.ValueKind is JsonValueKind.True or JsonValueKind.False ? (e.GetBoolean() ? 1 : 0) : (int)e.GetDouble();
    public static string? Str(this JsonElement e) => e.IsNull() ? null : e.GetString();
    public static JsonElement At(this JsonElement e, int i) => e[i];

    public static List<string> Strs(this JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToList();

    public static List<Piece>? Pieces(this JsonElement e) =>
        e.IsNull() ? null : e.EnumerateArray().Select(p => new Piece(p[0].Num(), p[1].Num(), p[2].Num())).ToList();

    public static List<Zoom> Zooms(this JsonElement e) =>
        e.IsNull() ? [] : e.EnumerateArray().Select(z => new Zoom(z[0].Num(), z[1].Num(), z[2].Num(), z[3].Num(), z[4].Num())).ToList();

    public static (int W, int H)? Size(this JsonElement e) => e.IsNull() ? null : (e[0].Int(), e[1].Int());

    /// <summary>The AUD / VHS / SLOW / ZOOM globals of a case.</summary>
    public static EffectSettings Effects(this JsonElement g)
    {
        var aud = g.GetProperty("AUD");
        var zoom = g.GetProperty("ZOOM");
        return new EffectSettings
        {
            Fast = aud.GetProperty("fast").GetString()!, Slow = aud.GetProperty("slow").GetString()!,
            Deep = aud.GetProperty("deep").GetBoolean(), High = aud.GetProperty("high").GetBoolean(),
            FastVol = aud.GetProperty("fast_vol").Int(), SlowVol = aud.GetProperty("slow_vol").Int(),
            VhsOn = g.GetProperty("VHS").GetProperty("on").GetBoolean(),
            SlowBlend = g.GetProperty("SLOW").GetProperty("blend").GetBoolean(),
            SlowRife = g.GetProperty("SLOW").GetProperty("rife").GetBoolean(),
            ZoomMotion = zoom.GetProperty("motion").GetBoolean(), ZoomLen = zoom.GetProperty("len").Num(),
            ZoomSound = zoom.GetProperty("sound").GetBoolean(), ZoomVol = zoom.GetProperty("vol").Int(),
            ZoomPlay = zoom.GetProperty("play").Zooms(),
        };
    }

    /// <summary>A clip dict as ClipRow.clip() builds it.</summary>
    public static ExportClip Clip(this JsonElement c) => new()
    {
        Path = c.GetProperty("path").GetString()!,
        Start = c.GetProperty("start").Num(), End = c.GetProperty("end").Num(),
        W = c.GetProperty("w").Int(), H = c.GetProperty("h").Int(), Fps = c.GetProperty("fps").Num(),
        Audio = c.Opt("audio")?.GetBoolean() ?? false,
        NAudio = c.Opt("naudio")?.Int(),
        Bitrate = c.Opt("bitrate")?.Num() ?? 0,
        Trc = c.Opt("trc")?.GetString(), Prim = c.Opt("prim")?.GetString(), Space = c.Opt("space")?.GetString(),
        Pieces = c.GetProperty("pieces").Pieces(),
        Zooms = c.GetProperty("zooms").Zooms(),
    };

    /// <summary>Rebuilds the clip from the timeline-level "row" through ExportClip.FromTimeline.</summary>
    public static ExportClip ClipFromRow(this JsonElement caseEl)
    {
        var row = caseEl.GetProperty("row");
        var clip = caseEl.GetProperty("clip");
        var info = new MediaInfo(clip.GetProperty("w").Int(), clip.GetProperty("h").Int(), clip.GetProperty("fps").Num(), 0,
            clip.GetProperty("naudio").Int(), clip.GetProperty("bitrate").Num(), 0,
            clip.Opt("trc")?.GetString(), clip.Opt("prim")?.GetString(), clip.Opt("space")?.GetString());
        var secs = row.Opt("sections")?.EnumerateArray()
            .Select(s => new SpeedSection(s.GetProperty("a").Num(), s.GetProperty("b").Num(), s.GetProperty("speed").Num())).ToList() ?? [];
        var zooms = row.Opt("zooms")?.EnumerateArray()
            .Select(z => new ZoomSection(z.GetProperty("a").Num(), z.GetProperty("b").Num(), z.GetProperty("x").Num(),
                                         z.GetProperty("y").Num(), z.GetProperty("w").Num())).ToList() ?? [];
        return ExportClip.FromTimeline(clip.GetProperty("path").GetString()!, clip.GetProperty("start").Num(),
                                       clip.GetProperty("end").Num(), info, secs, zooms);
    }

    public static IReadOnlyDictionary<int, RifeInput>? RifeMap(this JsonElement e) =>
        e.IsNull() ? null : e.EnumerateObject().ToDictionary(p => int.Parse(p.Name),
            p => new RifeInput(p.Value[0].Int(), p.Value[1].Int(), p.Value[2].Int()));

    public static IReadOnlyDictionary<int, IReadOnlyDictionary<int, RifeInput>>? RifeMaps(this JsonElement e) =>
        e.IsNull() ? null : e.EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.RifeMap()!);
}

/// <summary>Collects every mismatch of a fixture file so one test run shows all of them.</summary>
public sealed class Mismatches(string what)
{
    readonly List<string> list = [];
    int checkedCount;

    public void Check(bool ok, string message)
    {
        checkedCount++;
        if (!ok) list.Add(message);
    }

    public void Equal<T>(T expected, T actual, string where)
    {
        bool ok = expected is double de && actual is double da
            ? de.Equals(da)                                   // exact; NaN == NaN
            : EqualityComparer<T>.Default.Equals(expected, actual);
        Check(ok, $"{where}\n    expected: {expected}\n    actual:   {actual}");
    }

    public void Seq(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string where)
    {
        checkedCount++;
        if (expected.SequenceEqual(actual)) return;
        int i = 0;
        while (i < expected.Count && i < actual.Count && expected[i] == actual[i]) i++;
        string e = i < expected.Count ? expected[i] : "<end>";
        string a = i < actual.Count ? actual[i] : "<end>";
        list.Add($"{where}: first difference at item {i} (expected {expected.Count} items, got {actual.Count})\n" +
                 $"    expected: {e}\n    actual:   {a}");
    }

    public void Run(string where, Action body)
    {
        try { body(); }
        catch (Exception ex) { checkedCount++; list.Add($"{where}: threw {ex.GetType().Name}: {ex.Message}"); }
    }

    public int Count => checkedCount;

    public void AssertNone()
    {
        Assert.True(checkedCount > 0, $"{what}: nothing was checked");
        if (list.Count > 0)
            Assert.Fail($"{what}: {list.Count} of {checkedCount} checks failed. First ones:\n\n" + string.Join("\n\n", list.Take(8)));
    }
}
