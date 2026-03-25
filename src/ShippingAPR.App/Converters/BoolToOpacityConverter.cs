using System.Globalization;
using System.Windows.Data;

namespace ShippingAPR.App.Converters;

/// <summary>
/// Converts a boolean to opacity: true = 1.0, false = 0.5.
/// Used to show active/inactive state on toggle buttons.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.5;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
