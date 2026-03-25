namespace ShippingAPR.Core.Models;

/// <summary>
/// Represents a severe weather alert affecting a maritime area.
/// </summary>
public sealed record WeatherAlert
{
    public required string Id { get; init; }
    public required WeatherAlertType Type { get; init; }
    public required WeatherSeverity Severity { get; init; }
    public required string Description { get; init; }
    public required BoundingBox AffectedArea { get; init; }
    public double? WindSpeedKnots { get; init; }
    public double? WaveHeightMeters { get; init; }
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; init; }
}

public enum WeatherAlertType
{
    Storm,
    HighWaves,
    Fog,
    Gale,
    Hurricane,
    Ice,
    Tsunami
}

public enum WeatherSeverity
{
    Advisory,
    Watch,
    Warning,
    Extreme
}
