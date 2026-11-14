using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipShrinker.Core.Py;

/// <summary>Python type checks and conversions for values that came out of json.load().</summary>
public static class PyJson
{
    /// <summary>A JSON value with Python's type error (TypeError / ValueError / KeyError in the reference).</summary>
    public sealed class PyValueError(string message) : Exception(message);

    public static bool IsStr(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String;
    public static bool IsBool(JsonNode? n) => n is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False;

    /// <summary>isinstance(v, int) and not isinstance(v, bool): a JSON number written without fraction or exponent.</summary>
    public static bool IsPlainInt(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.ToJsonString().All(c => char.IsDigit(c) || c == '-');

    /// <summary>isinstance(v, int), which is also true for bools.</summary>
    public static bool IsIntOrBool(JsonNode? n) => IsPlainInt(n) || IsBool(n);

    /// <summary>isinstance(v, (int, float)) and not bool.</summary>
    public static bool IsNumber(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number;

    public static string? Str(JsonNode? n) => IsStr(n) ? n!.GetValue<string>() : null;
    public static bool? Bool(JsonNode? n) => IsBool(n) ? n!.GetValue<bool>() : null;

    public static long AsLong(JsonNode n) =>
        IsBool(n) ? (n.GetValue<bool>() ? 1 : 0) : long.Parse(n.ToJsonString(), CultureInfo.InvariantCulture);

    /// <summary>Python float(v): number, numeric string or bool; anything else raises.</summary>
    public static double Float(JsonNode? n)
    {
        switch (n)
        {
            case null: throw new PyValueError("float() argument must be a string or a real number, not 'NoneType'");
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                return double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                return Py.ParseFloat(v.GetValue<string>()) ?? throw new PyValueError($"could not convert string to float: '{v.GetValue<string>()}'");
            case JsonValue v when IsBool(v): return v.GetValue<bool>() ? 1.0 : 0.0;
            default: throw new PyValueError("float() argument must be a string or a real number");
        }
    }

    /// <summary>d[key] on a dict (KeyError / TypeError like Python).</summary>
    public static JsonNode? Item(JsonNode? d, string key)
    {
        if (d is not JsonObject o) throw new PyValueError("indices must be integers or slices, not str");
        if (!o.TryGetPropertyValue(key, out var v)) throw new PyValueError($"KeyError: '{key}'");
        return v;
    }

    /// <summary>Python str(v) of a JSON value.</summary>
    public static string PyStr(JsonNode? n) => n switch
    {
        null => "None",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.True => "True",
        JsonValue v when v.GetValueKind() == JsonValueKind.False => "False",
        JsonValue v when IsPlainInt(v) => v.ToJsonString(),
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => Repr(Float(v)),
        _ => n.ToJsonString(),
    };

    /// <summary>Python repr(float).</summary>
    public static string Repr(double x)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        string r = x.ToString("R", CultureInfo.InvariantCulture);
        double ax = Math.Abs(x);
        if (ax != 0 && (ax < 1e-4 || ax >= 1e16))
        {
            string e = x.ToString("E16", CultureInfo.InvariantCulture);
            // shortest mantissa that round-trips, Python style "1.5e-05"
            for (int p = 0; p <= 16; p++)
            {
                string s = x.ToString("E" + p, CultureInfo.InvariantCulture);
                if (double.Parse(s, CultureInfo.InvariantCulture) == x) { e = s; break; }
            }
            int ei = e.IndexOf('E');
            string mant = e[..ei].Contains('.') ? e[..ei].TrimEnd('0').TrimEnd('.') : e[..ei];
            int exp = int.Parse(e[(ei + 1)..], CultureInfo.InvariantCulture);
            return mant + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
        }
        if (!r.Contains('.') && !r.Contains('E')) r += ".0";
        return r;
    }
}
