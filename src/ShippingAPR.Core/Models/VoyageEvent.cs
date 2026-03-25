namespace ShippingAPR.Core.Models;

public sealed class VoyageEvent
{
    public required VoyageEventType Type { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Description { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? SpeedKnots { get; init; }
    public string? PortName { get; init; }
}

public enum VoyageEventType
{
    FirstSeen,
    SpeedChange,
    PortApproach,
    PortDeparture,
    CourseChange,
    Anchored,
    Moored,
    ZoneEntry,
    ZoneExit,
    PositionUpdate
}
