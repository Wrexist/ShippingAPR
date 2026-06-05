using ShippingAPR.Core.Constants;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Calculations;

public static class EtaCalculator
{

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
    public static EtaResult? Calculate(VesselPosition position, Port destination) =>
        Calculate(position, destination, DateTime.UtcNow);

    /// <summary>
    /// Overload accepting an explicit "now" timestamp for deterministic testing.
    /// </summary>
    public static EtaResult? Calculate(VesselPosition position, Port destination, DateTime utcNow)
    {
        if (position.SpeedOverGround < NavigationConstants.StationaryThresholdKnots ||
            position.SpeedOverGround > NavigationConstants.MaxPlausibleSpeedKnots)
            return null;

        var distance = HaversineCalculator.DistanceInNauticalMiles(
            position.Latitude, position.Longitude,
            destination.Latitude, destination.Longitude);

        if (distance < NavigationConstants.MinDistanceNm)
            return new EtaResult(0, TimeSpan.Zero, utcNow, 0, 0);

        var bearing = BearingCalculator.InitialBearing(
            position.Latitude, position.Longitude,
            destination.Latitude, destination.Longitude);

        var courseDeviation = BearingCalculator.AngleDifference(
            position.CourseOverGround, bearing);

        // A vessel whose course deviates 90° or more from the bearing to the
        // destination is moving perpendicular to (or away from) it and makes no
        // progress toward arrival on this course. Report "no ETA" rather than a
        // misleadingly finite one produced by clamping a negative cosine.
        if (courseDeviation >= 90.0)
            return null;

        // Project speed along bearing to destination.
        var cosDeviation = Math.Cos(courseDeviation * Math.PI / 180.0);
        var effectiveSpeed = Math.Max(
            position.SpeedOverGround * Math.Max(cosDeviation, NavigationConstants.CosineDeviationFloor),
            NavigationConstants.MinSpeedKnots);

        var hoursToArrival = distance / effectiveSpeed;
        var timeToArrival = TimeSpan.FromHours(hoursToArrival);
        var estimatedArrival = utcNow + timeToArrival;

        return new EtaResult(
            Math.Round(distance, 1),
            timeToArrival,
            estimatedArrival,
            Math.Round(courseDeviation, 1),
            Math.Round(effectiveSpeed, 1));
    }
}
