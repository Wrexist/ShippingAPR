using ShippingAPR.Core.Constants;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Providers;

/// <summary>
/// Builds a normalised <see cref="VesselPosition"/> from raw REST-provider fields,
/// applying the same sentinel/range handling as the WebSocket mapper so the polling
/// providers don't plot no-fix vessels at Null Island (0,0) or show "not available"
/// sentinels (SOG 102.3 / COG 360) as real values.
/// </summary>
public static class AisPositionNormalizer
{
    /// <summary>
    /// Returns a normalised position, or null when there is no usable fix
    /// (out-of-range or missing lat/lon, including the (0,0) default).
    /// </summary>
    public static VesselPosition? TryCreate(double lat, double lon, double sog, double cog, double heading)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
            return null;
        // Missing latitude/longitude default to 0 in the JSON readers — treat (0,0) as "no fix".
        if (lat == 0 && lon == 0)
            return null;

        var normalisedSog = sog is >= NavigationConstants.SpeedOverGroundNotAvailable or < 0 ? 0 : sog;
        var normalisedCog = cog >= NavigationConstants.CourseOverGroundNotAvailable ? 0 : cog;
        var normalisedHeading = heading is > 0 and < NavigationConstants.CourseOverGroundNotAvailable
            ? heading
            : normalisedCog;

        return new VesselPosition
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = normalisedSog,
            CourseOverGround = normalisedCog,
            TrueHeading = normalisedHeading,
            Timestamp = DateTime.UtcNow
        };
    }
}
