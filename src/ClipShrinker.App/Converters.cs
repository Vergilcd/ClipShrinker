using System.Globalization;
using System.Windows.Data;

namespace ClipShrinker.App;

/// <summary>true -> false and back (IsEnabled while something is not busy).</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>true -> Collapsed, false -> Visible.</summary>
public sealed class NotBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Null -> true (nothing is busy).</summary>
public sealed class IsNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value is null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>2.6: [key, dictionary] -> value (empty when the key is not in it). Used for the codec hints (2.9.1: without
/// the "- " in front, now that the name above it is plain words).</summary>
public sealed class HintLookupConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        values.Length == 2 && values[0] is string k && values[1] is IReadOnlyDictionary<string, string> d && d.TryGetValue(k, out var h)
            && h.Length > 0 ? h : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>2.9.1: the words shown for a saved value, which itself stays the same in settings and projects.
/// Parameter "codec" (HEVC (hevc_nvenc) -> HEVC · NVIDIA), "tracks" (the audio track modes), "reshint" (the second line
/// of a Resolution item in the open list).</summary>
public sealed class ShownTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string v = value as string ?? "";
        return (parameter as string) switch
        {
            "codec" => Core.Media.Encoders.ShownName(v),
            "tracks" => v switch { "Mix into one track" => "Mix all into one", "Keep as separate tracks" => "Keep separate", _ => v },
            "fxsound" => v switch                                // Effect settings: what the section sounds like
            {
                "VHS effect with sound" => "Tape-wind at start and end", "Normal sped-up sound" => "Just the sped-up sound",
                "Slow-mo effect with sound" => "Deep thump and swell", "Normal slow-mo sound" => "Just the slowed sound", _ => v,
            },
            "reshint" => v switch
            {
                "Auto" => "full size; smaller if the size limit is tight",
                "100%" => "always full size",
                "Original" => "full size (the clip's own resolution)",
                "4K" => "2160 lines high, never larger than the clip",
                "1440p" => "1440 lines high, never larger than the clip",
                "1080p" => "1080 lines high, never larger than the clip",
                "720p" => "720 lines high, never larger than the clip",
                "50%" => "half the width and height",
                _ => "",
            },
            _ => v,
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
