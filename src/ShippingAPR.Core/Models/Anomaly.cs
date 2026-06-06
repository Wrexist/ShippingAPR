namespace ShippingAPR.Core.Models;

public enum AnomalyType
{
    /// <summary>A vessel's position jumped implausibly far for the elapsed time (possible AIS spoofing / identity error).</summary>
    PositionJump,

    /// <summary>A moving vessel stopped transmitting AIS ("went dark").</summary>
    AisGap,

    /// <summary>A vessel lingered in a small area for an extended time (loitering).</summary>
    Loitering
}

/// <summary>A detected behavioural anomaly for a vessel.</summary>
public sealed record Anomaly(
    int Mmsi,
    AnomalyType Type,
    string Description,
    double Latitude,
    double Longitude,
    DateTime DetectedAtUtc);
