using ShippingAPR.Core.Calculations;

namespace ShippingAPR.Core.Models;

public sealed class GeofenceZone
{
    public required string Name { get; init; }
    public required BoundingBox Bounds { get; init; }
    public bool AlertOnEntry { get; init; } = true;
    public bool AlertOnExit { get; init; } = true;
    public string Color { get; init; } = "#6C63FF";

    /// <summary>
    /// Optional polygon vertices for a non-rectangular zone. When set (3+ points),
    /// containment uses the polygon and <see cref="Bounds"/> must be its bounding box,
    /// used as a broad-phase check and for transition hysteresis. When null, the zone is
    /// simply the rectangle <see cref="Bounds"/>.
    /// </summary>
    public IReadOnlyList<GeoPoint>? Polygon { get; init; }

    /// <summary>Whether the given point lies within the zone (polygon if present, else rectangle).</summary>
    public bool Contains(double latitude, double longitude)
    {
        if (Polygon is { Count: >= 3 } polygon)
            return Bounds.Contains(latitude, longitude)
                   && GeometryUtil.PointInPolygon(latitude, longitude, polygon);

        return Bounds.Contains(latitude, longitude);
    }

    /// <summary>
    /// Whether the point lies within the zone grown by the given margins. For polygons the
    /// margin is applied to the bounding box, which is sufficient for the boundary-flap
    /// hysteresis deadband used by area monitoring.
    /// </summary>
    public bool ContainsWithMargin(double latitude, double longitude, double latMargin, double lonMargin) =>
        Bounds.ContainsWithMargin(latitude, longitude, latMargin, lonMargin);
}

public sealed class VesselGeofenceEvent
{
    public required Vessel Vessel { get; init; }
    public required GeofenceZone Zone { get; init; }
    public required bool Entered { get; init; }
    public required DateTime Timestamp { get; init; }
}
