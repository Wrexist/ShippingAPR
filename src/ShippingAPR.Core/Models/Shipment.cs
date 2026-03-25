namespace ShippingAPR.Core.Models;

/// <summary>
/// A user-defined shipment tracking origin-to-destination cargo movement.
/// </summary>
public sealed class Shipment
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..16];
    public required string Name { get; init; }
    public required string OriginPortName { get; init; }
    public required string DestinationPortName { get; init; }
    public int? AssignedMmsi { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public enum ShipmentStatus
{
    Pending,
    InTransit,
    Delayed,
    Arrived,
    Cancelled
}

/// <summary>
/// Calculated progress of a shipment.
/// </summary>
public sealed class ShipmentProgress
{
    public required string ShipmentId { get; init; }
    public double PercentComplete { get; init; }
    public double DistanceTraveledNm { get; init; }
    public double DistanceRemainingNm { get; init; }
    public double TotalDistanceNm { get; init; }
    public TimeSpan? EstimatedTimeRemaining { get; init; }
    public DateTime? EstimatedArrival { get; init; }
    public string? AssignedVesselName { get; init; }
    public double CurrentSpeedKnots { get; init; }
}
