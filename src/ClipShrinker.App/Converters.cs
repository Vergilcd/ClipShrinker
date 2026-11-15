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

/// <summary>2.6: [key, dictionary] -> " - value" (empty when the key is not in it). Used for the codec hints.</summary>
public sealed class HintLookupConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        values.Length == 2 && values[0] is string k && values[1] is IReadOnlyDictionary<string, string> d && d.TryGetValue(k, out var h)
            && h.Length > 0 ? "- " + h : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
