using ShippingAPR.Core.Constants;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Calculations;

/// <summary>
/// Projects a vessel's future positions based on current course and speed.
/// Uses great-circle forward projection for accuracy at maritime distances.
/// </summary>
public static class RouteProjectionCalculator
{
    private const double EarthRadiusNm = 3440.065;

    /// <summary>
    /// Projects a series of future positions along the vessel's current course.
    /// </summary>
    /// <param name="position">Current vessel position with COG and SOG.</param>
    /// <param name="horizonMinutes">How far into the future to project (in minutes).</param>
    /// <param name="stepCount">Number of intermediate points to generate.</param>
    /// <returns>List of projected (Latitude, Longitude) points, or empty if vessel is stationary.</returns>
    public static IReadOnlyList<(double Latitude, double Longitude)> Project(
        VesselPosition position,
        double horizonMinutes = 60,
        int stepCount = 12)
    {
        if (position.SpeedOverGround < NavigationConstants.StationaryThresholdKnots ||
            position.SpeedOverGround > NavigationConstants.MaxPlausibleSpeedKnots)
            return [];

        var result = new List<(double, double)>(stepCount);
        var stepMinutes = horizonMinutes / stepCount;
        var cogRad = DegreesToRadians(position.CourseOverGround);
        var lat1Rad = DegreesToRadians(position.Latitude);
        var lon1Rad = DegreesToRadians(position.Longitude);

        for (var i = 1; i <= stepCount; i++)
        {
            var distanceNm = position.SpeedOverGround * (stepMinutes * i) / 60.0;
            var angularDistance = distanceNm / EarthRadiusNm;

            var lat2Rad = Math.Asin(
                Math.Sin(lat1Rad) * Math.Cos(angularDistance) +
                Math.Cos(lat1Rad) * Math.Sin(angularDistance) * Math.Cos(cogRad));

            var lon2Rad = lon1Rad + Math.Atan2(
                Math.Sin(cogRad) * Math.Sin(angularDistance) * Math.Cos(lat1Rad),
                Math.Cos(angularDistance) - Math.Sin(lat1Rad) * Math.Sin(lat2Rad));

            var lat2 = RadiansToDegrees(lat2Rad);
            var lon2 = RadiansToDegrees(lon2Rad);

            // Normalize longitude to [-180, 180]
            lon2 = ((lon2 + 540) % 360) - 180;

            // Clamp latitude (shouldn't exceed bounds with valid input, but safety first)
            lat2 = Math.Clamp(lat2, -90, 90);

            result.Add((lat2, lon2));
        }

        return result;
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) =>
        radians * 180.0 / Math.PI;
}
