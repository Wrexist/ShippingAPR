using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Calculations;

/// <summary>
/// Calculates Closest Point of Approach (CPA) and Time to CPA (TCPA)
/// between two vessels using linear motion projection.
/// </summary>
public static class CpaCalculator
{
    private const double NauticalMilesPerDegree = 60.0;
    private const double MinSpeedKnots = 0.5;

    /// <summary>
    /// Compute CPA and TCPA for two vessels based on their current positions, courses, and speeds.
    /// Returns null if either vessel is stationary or TCPA is in the past.
    /// </summary>
    public static CpaResult? Calculate(
        int mmsi1, double lat1, double lon1, double cog1, double sog1,
        int mmsi2, double lat2, double lon2, double cog2, double sog2)
    {
        // Skip stationary vessels
        if (sog1 < MinSpeedKnots || sog2 < MinSpeedKnots)
            return null;

        // Convert to planar coordinates (nautical miles from origin)
        // Using simple equirectangular projection around the midpoint latitude
        var midLat = (lat1 + lat2) / 2.0;
        var cosLat = Math.Cos(midLat * Math.PI / 180.0);

        var x1 = lon1 * cosLat * NauticalMilesPerDegree;
        var y1 = lat1 * NauticalMilesPerDegree;
        var x2 = lon2 * cosLat * NauticalMilesPerDegree;
        var y2 = lat2 * NauticalMilesPerDegree;

        // Convert COG/SOG to velocity components (knots)
        var cog1Rad = cog1 * Math.PI / 180.0;
        var cog2Rad = cog2 * Math.PI / 180.0;
        var vx1 = sog1 * Math.Sin(cog1Rad);
        var vy1 = sog1 * Math.Cos(cog1Rad);
        var vx2 = sog2 * Math.Sin(cog2Rad);
        var vy2 = sog2 * Math.Cos(cog2Rad);

        // Relative position and velocity
        var dx = x2 - x1;
        var dy = y2 - y1;
        var dvx = vx2 - vx1;
        var dvy = vy2 - vy1;

        // TCPA = -(dP · dV) / (dV · dV)
        var dvDotDv = dvx * dvx + dvy * dvy;
        if (dvDotDv < 1e-10)
            return null; // Parallel courses at same speed — no convergence

        var dpDotDv = dx * dvx + dy * dvy;
        var tcpaHours = -dpDotDv / dvDotDv;

        // Only interested in future CPA
        if (tcpaHours < 0)
            return null;

        // Calculate CPA distance
        var cpaX = dx + dvx * tcpaHours;
        var cpaY = dy + dvy * tcpaHours;
        var cpaDist = Math.Sqrt(cpaX * cpaX + cpaY * cpaY);

        return new CpaResult(
            mmsi1,
            mmsi2,
            cpaDist,
            TimeSpan.FromHours(tcpaHours));
    }
}
