using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Calculations;

public static class EtaCalculator
{
    private const double MinSpeedKnots = 0.5;
    private const double StationaryThreshold = 0.3;

    /// <summary>
    /// Calculates ETA using course-corrected speed projection.
    /// This is more accurate than naive distance/speed because it accounts
    /// for vessels not heading directly toward their destination.
    ///
    /// Algorithm:
    /// 1. Distance = Haversine(current pos → destination port)
    /// 2. Bearing = InitialBearing(current pos → destination port)
    /// 3. CourseDeviation = angular difference between COG and bearing
    /// 4. EffectiveSpeed = SOG * cos(courseDeviation) — projected speed along bearing
    /// 5. ETA = distance / effectiveSpeed
    /// </summary>
    public static EtaResult? Calculate(VesselPosition position, Port destination)
    {
        if (position.SpeedOverGround < StationaryThreshold)
            return null;

        var distance = HaversineCalculator.DistanceInNauticalMiles(
            position.Latitude, position.Longitude,
            destination.Latitude, destination.Longitude);

        if (distance < 0.1)
            return new EtaResult(0, TimeSpan.Zero, DateTime.UtcNow, 0, 0);

        var bearing = BearingCalculator.InitialBearing(
            position.Latitude, position.Longitude,
            destination.Latitude, destination.Longitude);

        var courseDeviation = BearingCalculator.AngleDifference(
            position.CourseOverGround, bearing);

        // Project speed along bearing to destination.
        // If deviation > 90°, vessel is moving away — use minimum speed
        // to still provide an estimate (though it will be very large).
        var cosDeviation = Math.Cos(courseDeviation * Math.PI / 180.0);
        var effectiveSpeed = Math.Max(
            position.SpeedOverGround * Math.Max(cosDeviation, 0.1),
            MinSpeedKnots);

        var hoursToArrival = distance / effectiveSpeed;
        var timeToArrival = TimeSpan.FromHours(hoursToArrival);
        var estimatedArrival = DateTime.UtcNow + timeToArrival;

        return new EtaResult(
            Math.Round(distance, 1),
            timeToArrival,
            estimatedArrival,
            Math.Round(courseDeviation, 1),
            Math.Round(effectiveSpeed, 1));
    }
}
