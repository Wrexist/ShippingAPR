using System.Globalization;
using System.Windows.Data;
using ShippingAPR.App.Resources;

namespace ShippingAPR.App.Converters;

public sealed class SelectAreaTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelecting)
            return isSelecting ? Strings.ClearSelection : Strings.SelectArea;
        return Strings.SelectArea;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
