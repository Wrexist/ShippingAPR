using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Calculations;

/// <summary>Planar geometry helpers for geofencing.</summary>
public static class GeometryUtil
{
    /// <summary>
    /// Tests whether a point lies inside a polygon using the ray-casting (even-odd) rule.
    /// Treats longitude as x and latitude as y. A polygon of fewer than three vertices is
    /// never considered to contain a point. The polygon is treated as implicitly closed
    /// (the last vertex connects back to the first).
    /// </summary>
    public static bool PointInPolygon(double latitude, double longitude, IReadOnlyList<GeoPoint> polygon)
    {
        var n = polygon.Count;
        if (n < 3) return false;

        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var yi = polygon[i].Latitude;
            var xi = polygon[i].Longitude;
            var yj = polygon[j].Latitude;
            var xj = polygon[j].Longitude;

            var crossesLatitude = (yi > latitude) != (yj > latitude);
            if (crossesLatitude &&
                longitude < (xj - xi) * (latitude - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
