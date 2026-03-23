using System.Globalization;
using System.Windows.Data;

namespace ShippingAPR.App.Converters;

public sealed class SpeedToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double speed)
            return $"{speed:F1} kn";
        return "-- kn";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CourseToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double course)
        {
            var cardinal = course switch
            {
                >= 337.5 or < 22.5 => "N",
                >= 22.5 and < 67.5 => "NE",
                >= 67.5 and < 112.5 => "E",
                >= 112.5 and < 157.5 => "SE",
                >= 157.5 and < 202.5 => "S",
                >= 202.5 and < 247.5 => "SW",
                >= 247.5 and < 292.5 => "W",
                _ => "NW"
            };
            return $"{course:F0}° {cardinal}";
        }
        return "-- °";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EtaToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m";
        }
        return "--";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
