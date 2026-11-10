using System.Globalization;
using System.Windows.Data;

namespace Nexify.App.Converters;

/// <summary>Upper-cases a string (the NEXIFY PLAYER wordmark is set in capitals, spec 3.5).</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? s.ToUpper(culture) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
