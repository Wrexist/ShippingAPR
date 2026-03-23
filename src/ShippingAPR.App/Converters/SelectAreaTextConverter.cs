using System.Globalization;
using System.Windows.Data;

namespace ShippingAPR.App.Converters;

public sealed class SelectAreaTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelecting)
            return isSelecting ? "Cancel Selection" : "Select Area";
        return "Select Area";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
