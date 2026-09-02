using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AirControl.App.Converters;

public class ClippingToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.Red : Brushes.LimeGreen;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
