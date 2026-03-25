using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

/// <summary>
/// Real-time emission estimate for a vessel based on type, size, and speed.
/// Uses IMO reference formulas and cubic speed-power relationships.
/// </summary>
public sealed class EmissionEstimate
{
    public double FuelTonnesPerHour { get; init; }
    public double Co2TonnesPerHour { get; init; }
    public double SoxKgPerHour { get; init; }
    public double NoxKgPerHour { get; init; }

    /// <summary>CII rating: A (best) through E (worst).</summary>
    public char CiiRating { get; init; }
}

/// <summary>
/// Reference emission profile for a vessel type, defining fuel consumption
/// characteristics relative to speed and size.
/// </summary>
public sealed record EmissionProfile(
    VesselType VesselType,
    double ReferenceFuelTonnesPerHour,
    double ReferenceSpeedKnots,
    double ReferenceLengthMeters,
    double SpeedExponent = 3.0);

/// <summary>
/// Fleet-level emissions summary for the dashboard.
/// </summary>
public sealed class FleetEmissionsSummary
{
    public double TotalCo2TonnesPerHour { get; init; }
    public double TotalFuelTonnesPerHour { get; init; }
    public int VesselsWithEstimates { get; init; }
    public double AverageCo2PerVessel { get; init; }
    public string HighestEmitterName { get; init; } = "";
    public double HighestEmitterCo2 { get; init; }
    public string CleanestVesselName { get; init; } = "";
    public double CleanestVesselCo2 { get; init; }
    public Dictionary<char, int> CiiDistribution { get; init; } = new();
}
