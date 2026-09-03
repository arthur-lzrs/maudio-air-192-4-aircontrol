using System.Globalization;
using System.Windows.Data;

namespace AirControl.App.Converters;

public class TrimDbToDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double db
            ? double.IsNegativeInfinity(db) ? "-∞ dB" : string.Format(culture, "{0:0.0} dB", db)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
