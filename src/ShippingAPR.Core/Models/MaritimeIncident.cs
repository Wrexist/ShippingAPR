namespace ShippingAPR.Core.Models;

/// <summary>
/// A maritime incident, news item, or navigational warning.
/// </summary>
public sealed class MaritimeIncident
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public IncidentSeverity Severity { get; init; }
    public IncidentCategory Category { get; init; }
    public required string Source { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; init; }
    public bool IsActive => ExpiresAt is null || ExpiresAt > DateTime.UtcNow;
}

public enum IncidentSeverity
{
    Info,
    Advisory,
    Warning,
    Critical
}

public enum IncidentCategory
{
    NavigationalWarning,
    Piracy,
    Weather,
    PortClosure,
    EnvironmentalHazard,
    SecurityAlert,
    GeneralNews
}
