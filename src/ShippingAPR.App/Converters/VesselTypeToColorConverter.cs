using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ShippingAPR.Core;
using ShippingAPR.Core.Enums;

namespace ShippingAPR.App.Converters;

public sealed class VesselTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var type = value is VesselType vt ? vt : VesselType.Unknown;
        var (r, g, b) = VesselTypeColors.GetRgb(type);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
