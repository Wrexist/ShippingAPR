namespace ShippingAPR.Core.Models;

/// <summary>
/// Defines a maritime chokepoint/strait with entry and exit boundaries.
/// </summary>
public sealed record Chokepoint(
    string Name,
    BoundingBox Bounds,
    double CenterLatitude,
    double CenterLongitude,
    int TypicalTransitMinutes);

/// <summary>
/// Real-time status of a maritime chokepoint.
/// </summary>
public sealed class ChokepointStatus
{
    public required string Name { get; init; }
    public int VesselsInTransit { get; init; }
    public int TransitsLast24h { get; init; }
    public double AvgSpeedKnots { get; init; }
    public ChokepointCongestion CongestionLevel { get; init; }
    public int TypicalTransitMinutes { get; init; }
    public double CenterLatitude { get; init; }
    public double CenterLongitude { get; init; }
    public List<ChokepointTransitRecord> RecentTransits { get; init; } = [];
}

public enum ChokepointCongestion
{
    Low,
    Moderate,
    High,
    VeryHigh
}

/// <summary>
/// Record of a vessel transiting through a chokepoint.
/// </summary>
public sealed class ChokepointTransitRecord
{
    public int Mmsi { get; init; }
    public required string VesselName { get; init; }
    public required string VesselType { get; init; }
    public double SpeedKnots { get; init; }
    public DateTime Timestamp { get; init; }
}
