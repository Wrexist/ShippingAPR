namespace ShippingAPR.Core.Models;

public sealed class GeofenceZone
{
    public required string Name { get; init; }
    public required BoundingBox Bounds { get; init; }
    public bool AlertOnEntry { get; init; } = true;
    public bool AlertOnExit { get; init; } = true;
    public string Color { get; init; } = "#6C63FF";
}

public sealed class VesselGeofenceEvent
{
    public required Vessel Vessel { get; init; }
    public required GeofenceZone Zone { get; init; }
    public required bool Entered { get; init; }
    public required DateTime Timestamp { get; init; }
}
