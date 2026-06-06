using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShippingAPR.App.Converters;

/// <summary>
/// Converts a 0–100 percentage into a star <see cref="GridLength"/> for a two-column
/// fill bar (a robust, measure-free progress bar). With ConverterParameter "remainder"
/// it returns (100 − percent); otherwise it returns the clamped percent.
/// </summary>
public sealed class PercentToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is IConvertible c
            ? System.Convert.ToDouble(c, CultureInfo.InvariantCulture)
            : 0;
        percent = Math.Clamp(percent, 0, 100);
        var portion = (parameter as string) == "remainder" ? 100 - percent : percent;
        return new GridLength(portion, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
