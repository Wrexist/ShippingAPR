using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

/// <summary>
/// A single vessel encounter logged in the ship-spotter's journal.
/// </summary>
public sealed class Encounter
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public int Mmsi { get; init; }
    public string VesselName { get; init; } = "";
    public VesselType VesselType { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double SpeedKnots { get; init; }
    public string? Destination { get; init; }
    public string? CountryCode { get; init; }
    public int LengthOverall { get; init; }

    // User-editable fields
    public string UserNotes { get; set; } = "";
    public int Rating { get; set; } // 1-5, 0 = unrated
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Journal statistics summary.
/// </summary>
public sealed class JournalStats
{
    public int TotalEncounters { get; init; }
    public int UniqueCountries { get; init; }
    public int UniqueTypes { get; init; }
    public string? RarestTypeName { get; init; }
    public string? MostSpottedCountry { get; init; }
    public DateTime? FirstEncounterDate { get; init; }
    public DateTime? LatestEncounterDate { get; init; }
}
