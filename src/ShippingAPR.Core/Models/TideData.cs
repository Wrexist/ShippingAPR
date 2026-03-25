namespace ShippingAPR.Core.Models;

/// <summary>
/// Tide and sea level data for a port.
/// </summary>
public sealed record TideData
{
    public required string PortLocode { get; init; }
    public required string PortName { get; init; }
    public double CurrentLevelMeters { get; init; }
    public double HighTideMeters { get; init; }
    public double LowTideMeters { get; init; }
    public DateTime? NextHighTide { get; init; }
    public DateTime? NextLowTide { get; init; }
    public TideState State { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public enum TideState
{
    Rising,
    Falling,
    Slack,
    Unknown
}
