using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

public sealed class AlertRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..16];
    public required string Name { get; init; }
    public bool IsEnabled { get; set; } = true;

    // Conditions — all non-null conditions must match (AND logic)
    public VesselType? VesselTypeFilter { get; init; }
    public double? MinSpeedKnots { get; init; }
    public double? MaxSpeedKnots { get; init; }
    public string? FlagFilter { get; init; }
    public HashSet<int>? MmsiFilter { get; init; }
    public AlertZone? ZoneFilter { get; init; }
    public NavigationalStatus? StatusFilter { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public bool Matches(Vessel vessel)
    {
        if (!IsEnabled) return false;

        if (VesselTypeFilter.HasValue && vessel.Type != VesselTypeFilter.Value)
            return false;

        // Conditions that depend on live position data cannot be satisfied by a vessel
        // that hasn't reported a position yet — treat them as non-matching rather than
        // letting the rule fire vacuously on a positionless vessel.
        var needsPosition = MinSpeedKnots.HasValue || MaxSpeedKnots.HasValue
            || StatusFilter.HasValue || ZoneFilter is not null;
        if (needsPosition && vessel.CurrentPosition is null)
            return false;

        if (vessel.CurrentPosition is { } pos)
        {
            if (MinSpeedKnots.HasValue && pos.SpeedOverGround < MinSpeedKnots.Value)
                return false;
            if (MaxSpeedKnots.HasValue && pos.SpeedOverGround > MaxSpeedKnots.Value)
                return false;
            if (StatusFilter.HasValue && pos.Status != StatusFilter.Value)
                return false;
        }

        if (FlagFilter is not null && vessel.StaticData?.CountryCode != FlagFilter)
            return false;

        if (MmsiFilter is { Count: > 0 } && !MmsiFilter.Contains(vessel.Mmsi))
            return false;

        if (ZoneFilter is not null)
        {
            if (vessel.CurrentPosition is not { } p)
                return false; // Cannot evaluate zone filter without position data

            if (p.Latitude < ZoneFilter.MinLat || p.Latitude > ZoneFilter.MaxLat ||
                p.Longitude < ZoneFilter.MinLon || p.Longitude > ZoneFilter.MaxLon)
                return false;
        }

        return true;
    }
}

public sealed record AlertZone(
    string Name,
    double MinLat,
    double MaxLat,
    double MinLon,
    double MaxLon);
