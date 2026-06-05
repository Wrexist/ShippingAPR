using System.Globalization;

namespace ShippingAPR.Core.Formatting;

/// <summary>
/// Formats latitude/longitude with the correct hemisphere suffix. Avoids the common
/// bug of hardcoding "°N, °E", which is wrong for the southern and western hemispheres
/// (e.g. a vessel in Sydney would otherwise print "-33.9°N, 151.2°E").
/// </summary>
public static class CoordinateFormatter
{
    public static string Format(double latitude, double longitude, int decimals = 4)
    {
        var latHemisphere = latitude >= 0 ? "N" : "S";
        var lonHemisphere = longitude >= 0 ? "E" : "W";
        var fmt = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        var lat = Math.Abs(latitude).ToString(fmt, CultureInfo.InvariantCulture);
        var lon = Math.Abs(longitude).ToString(fmt, CultureInfo.InvariantCulture);
        return $"{lat}°{latHemisphere}, {lon}°{lonHemisphere}";
    }
}
