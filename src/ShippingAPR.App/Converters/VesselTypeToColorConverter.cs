using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ShippingAPR.Core.Enums;

namespace ShippingAPR.App.Converters;

public sealed class VesselTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not VesselType type)
            return new SolidColorBrush(Color.FromRgb(158, 158, 158));

        var color = type switch
        {
            VesselType.Cargo => Color.FromRgb(76, 175, 80),
            VesselType.Tanker => Color.FromRgb(255, 87, 34),
            VesselType.Passenger => Color.FromRgb(33, 150, 243),
            VesselType.Fishing => Color.FromRgb(255, 152, 0),
            VesselType.Tug or VesselType.Pilot => Color.FromRgb(156, 39, 176),
            VesselType.Military => Color.FromRgb(96, 125, 139),
            VesselType.Sailing or VesselType.PleasureCraft => Color.FromRgb(0, 188, 212),
            VesselType.HighSpeedCraft => Color.FromRgb(255, 235, 59),
            VesselType.SearchAndRescue => Color.FromRgb(244, 67, 54),
            _ => Color.FromRgb(158, 158, 158)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
