namespace ShippingAPR.Core.Calculations;

public static class BearingCalculator
{
    /// <summary>
    /// Calculates the initial bearing (forward azimuth) from point 1 to point 2.
    /// Returns bearing in degrees [0, 360).
    /// </summary>
    public static double InitialBearing(
        double lat1, double lon1,
        double lat2, double lon2)
    {
        var lat1Rad = DegreesToRadians(lat1);
        var lat2Rad = DegreesToRadians(lat2);
        var dLonRad = DegreesToRadians(lon2 - lon1);

        var y = Math.Sin(dLonRad) * Math.Cos(lat2Rad);
        var x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLonRad);

        var bearingRad = Math.Atan2(y, x);
        var bearingDeg = RadiansToDegrees(bearingRad);

        return (bearingDeg + 360) % 360;
    }

    /// <summary>
    /// Returns the absolute angular difference between two bearings,
    /// always in range [0, 180].
    /// </summary>
    public static double AngleDifference(double bearing1, double bearing2)
    {
        var diff = Math.Abs(bearing1 - bearing2) % 360;
        return diff > 180 ? 360 - diff : diff;
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) =>
        radians * 180.0 / Math.PI;
}
