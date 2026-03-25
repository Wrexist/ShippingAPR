using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Selects a "Vessel of the Day" spotlight based on interesting vessel characteristics.
/// </summary>
public sealed class SpotlightService
{
    private readonly IVesselStore _vesselStore;
    private Vessel? _spotlightVessel;
    private DateTime _lastSelection;
    private string _spotlightReason = "";

    public Vessel? SpotlightVessel => _spotlightVessel;
    public string SpotlightReason => _spotlightReason;

    public SpotlightService(IVesselStore vesselStore)
    {
        _vesselStore = vesselStore;
    }

    /// <summary>
    /// Selects a spotlight vessel. Changes at most once per hour.
    /// </summary>
    public (Vessel? Vessel, string Reason) GetSpotlight()
    {
        if (_spotlightVessel is not null && DateTime.UtcNow - _lastSelection < TimeSpan.FromHours(1))
            return (_spotlightVessel, _spotlightReason);

        var vessels = _vesselStore.Vessels.Values
            .Where(v => v.CurrentPosition is not null && v.StaticData is not null)
            .ToList();

        if (vessels.Count == 0)
            return (null, "");

        // Pick the most interesting vessel using a scoring system
        var scored = vessels.Select(v =>
        {
            var score = 0;
            var reasons = new List<string>();

            // Large ships are interesting
            if (v.StaticData?.LengthOverall > 300)
            {
                score += 30;
                reasons.Add($"massive {v.StaticData.LengthOverall}m vessel");
            }
            else if (v.StaticData?.LengthOverall > 200)
            {
                score += 15;
                reasons.Add($"large {v.StaticData.LengthOverall}m vessel");
            }

            // Fast ships
            if (v.CurrentPosition?.SpeedOverGround > 20)
            {
                score += 25;
                reasons.Add($"speeding at {v.CurrentPosition.SpeedOverGround:F1} kn");
            }

            // Ships with known destination
            if (!string.IsNullOrEmpty(v.StaticData?.Destination))
            {
                score += 10;
                reasons.Add($"heading to {v.StaticData.Destination}");
            }

            // Rare vessel types
            if (v.Type is Core.Enums.VesselType.Military or Core.Enums.VesselType.SearchAndRescue
                or Core.Enums.VesselType.MedicalTransport)
            {
                score += 20;
                reasons.Add($"rare {v.Type} vessel");
            }

            // Passenger ships are popular
            if (v.Type == Core.Enums.VesselType.Passenger)
            {
                score += 15;
                reasons.Add("passenger vessel");
            }

            return (Vessel: v, Score: score, Reason: string.Join(", ", reasons));
        })
        .OrderByDescending(x => x.Score)
        .FirstOrDefault();

        _spotlightVessel = scored.Vessel;
        _spotlightReason = scored.Reason.Length > 0 ? scored.Reason : "Vessel in view";
        _lastSelection = DateTime.UtcNow;

        return (_spotlightVessel, _spotlightReason);
    }
}
