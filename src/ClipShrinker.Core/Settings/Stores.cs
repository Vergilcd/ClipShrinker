using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipShrinker.Core.Settings;

public sealed record Preset(string Name, double Mbps, string Key);

/// <summary>presets.json: {"presets": [{"name", "mbps", "key"}]} (reference load_presets / save_presets).</summary>
public static class PresetStore
{
    /// <summary>Like the reference: invalid entries are skipped, and a broken entry stops reading
    /// (the presets before it are kept).</summary>
    /// <param name="reserved">Keys a preset may not have (default: the 1.0.8 list; the app passes its current shortcut keys).</param>
    public static List<Preset> Load(string path, IReadOnlySet<string>? reserved = null)
    {
        reserved ??= C.ReservedKeys;
        var res = new List<Preset>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("presets", out var list)) return res;
            if (!IsTruthy(list)) return res;
            foreach (var d in list.EnumerateArray())
            {
                string name = PyStr(Get(d, "name") ?? "").Trim();
                double mbps = PyFloat(Get(d, "mbps") ?? 0);
                string key = PyStr(Get(d, "key") ?? "").ToLowerInvariant();
                if (name.Length > 0 && mbps > 0 && res.All(o => o.Name != name))
                    res.Add(new Preset(name, mbps, reserved.Contains(key) ? "" : key));
            }
        }
        catch (Exception e) when (e is JsonException or IOException or FormatException or InvalidOperationException
                                      or UnauthorizedAccessException or KeyNotFoundException)
        {
        }
        return res;
    }

    /// <summary>Checks a new or edited preset (reference _preset_edit ok()). Returns the parsed preset, or the
    /// error box (title, text). index = the edited preset, null = a new one.</summary>
    public static (Preset? Preset, string? Title, string? Error) Validate(IReadOnlyList<Preset> list, int? index, string name,
                                                                          string mbpsText, string key, IReadOnlySet<string>? reserved = null)
    {
        reserved ??= C.ReservedKeys;
        name = name.Trim();
        var mbps = Py.Py.ParseFloat(mbpsText.Replace(",", "."));
        if (mbps is not { } v || !(0.1 <= v && v <= 1000)) return (null, "Bitrate", "Enter the bitrate in Mbps, e.g. 20.");
        key = key.Trim().ToLowerInvariant();
        if (name.Length == 0) return (null, "Name", "Give the preset a name.");
        if (list.Where((q, i) => i != index).Any(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase)))
            return (null, "Name", "There is already a preset with this name.");
        if (reserved.Contains(key)) return (null, "Hotkey", "This key already does something else - pick another one.");
        if (key.Length > 0 && list.Where((q, i) => i != index).FirstOrDefault(q => q.Key == key) is { } clash)
            return (null, "Hotkey", $"The key is already used by \"{clash.Name}\".");
        return (new Preset(name, v, key), null, null);
    }

    public static void Save(string path, IEnumerable<Preset> presets)
    {
        try
        {
            var arr = new JsonArray(presets.Select(p => (JsonNode)new JsonObject
                { ["name"] = p.Name, ["mbps"] = p.Mbps, ["key"] = p.Key }).ToArray());
            SafeFile.WriteAllText(path, new JsonObject { ["presets"] = arr }.ToJsonString(SettingsStore.JsonOut));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static object? Get(JsonElement d, string key)
    {
        if (d.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("'" + d.ValueKind + "' object has no attribute 'get'");
        return d.TryGetProperty(key, out var v) ? v : null;
    }

    static bool IsTruthy(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.GetArrayLength() > 0,
        JsonValueKind.Null or JsonValueKind.False => false,
        _ => throw new InvalidOperationException("presets is not a list"),
    };

    /// <summary>Python str(x) of a JSON value.</summary>
    static string PyStr(object v) => v switch
    {
        string s => s,
        int i => i.ToString(CultureInfo.InvariantCulture),
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString()!,
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => "None",
            _ => e.GetRawText(),
        },
        _ => v.ToString() ?? "",
    };

    /// <summary>Python float(x) of a JSON value (FormatException where Python raises).</summary>
    static double PyFloat(object v) => v switch
    {
        int i => i,
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String => Py.Py.ParseFloat(e.GetString()) ?? throw new FormatException(),
            JsonValueKind.True => 1.0,
            JsonValueKind.False => 0.0,
            _ => throw new FormatException(),
        },
        _ => throw new FormatException(),
    };
}

/// <summary>settings.json: a flat JSON object, kept as is so unknown keys survive (reference load_settings / save_settings).</summary>
public static class SettingsStore
{
    /// <summary>Indented, with '+' and non-ASCII text written as is (like Python's json.dump), not as \u escapes.</summary>
    internal static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonObject Load(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            // 2.9.5: keep the damaged file next to it (the next save would overwrite it with the defaults)
            try { File.Copy(path, path + ".broken", overwrite: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Save(string path, JsonObject settings)
    {
        try
        {
            SafeFile.WriteAllText(path, settings.ToJsonString(JsonOut));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>2.9.5: writes a file whole or not at all: the text goes to "name.tmp" first and is then moved over the
/// old file (a rename on the same disk), so a crash or a full disk mid-write leaves the old file as it was.</summary>
public static class SafeFile
{
    public static void WriteAllText(string path, string text)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(f, new System.Text.UTF8Encoding(false)))
            {
                w.Write(text);
                w.Flush();
                f.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
