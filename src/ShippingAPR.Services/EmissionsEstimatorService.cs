using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Estimates vessel emissions (CO2, SOx, NOx) based on vessel type, dimensions, and speed.
/// Uses IMO-aligned cubic speed-power curves and standard emission factors.
/// </summary>
public sealed class EmissionsEstimatorService
{
    // IMO emission factors for HFO (Heavy Fuel Oil) - tonnes CO2 per tonne fuel
    private const double Co2FactorPerTonneFuel = 3.114;
    // SOx factor: ~2.7% sulphur content for HFO → ~54 kg SOx per tonne fuel (pre-2020 scrubber)
    // Post-2020 IMO (0.5% VLSFO): ~10 kg SOx per tonne fuel
    private const double SoxFactorKgPerTonneFuel = 10.0;
    // NOx: ~80 kg per tonne fuel for slow-speed diesel (Tier II)
    private const double NoxFactorKgPerTonneFuel = 80.0;

    // Minimum speed threshold (knots) to produce meaningful estimates
    private const double MinSpeedKnots = 0.5;

    private static readonly Dictionary<VesselType, EmissionProfile> Profiles = new()
    {
        // Reference values: typical fuel consumption at design speed for a reference-sized vessel
        // Based on IMO Fourth GHG Study reference data
        [VesselType.Cargo] = new(VesselType.Cargo, 1.8, 14.0, 200.0),
        [VesselType.Tanker] = new(VesselType.Tanker, 2.2, 14.5, 250.0),
        [VesselType.Passenger] = new(VesselType.Passenger, 3.0, 20.0, 280.0),
        [VesselType.Fishing] = new(VesselType.Fishing, 0.15, 10.0, 30.0),
        [VesselType.Tug] = new(VesselType.Tug, 0.4, 12.0, 35.0),
        [VesselType.Towing] = new(VesselType.Towing, 0.35, 10.0, 40.0),
        [VesselType.HighSpeedCraft] = new(VesselType.HighSpeedCraft, 2.5, 30.0, 100.0, 2.5),
        [VesselType.Pilot] = new(VesselType.Pilot, 0.1, 12.0, 25.0),
        [VesselType.SearchAndRescue] = new(VesselType.SearchAndRescue, 0.3, 20.0, 40.0),
        [VesselType.Military] = new(VesselType.Military, 2.0, 18.0, 150.0),
        [VesselType.Sailing] = new(VesselType.Sailing, 0.02, 6.0, 20.0),
        [VesselType.PleasureCraft] = new(VesselType.PleasureCraft, 0.05, 10.0, 15.0),
        [VesselType.Dredging] = new(VesselType.Dredging, 0.8, 8.0, 80.0),
        [VesselType.PortTender] = new(VesselType.PortTender, 0.1, 10.0, 25.0),
        [VesselType.MedicalTransport] = new(VesselType.MedicalTransport, 0.3, 14.0, 60.0),
        [VesselType.WingInGround] = new(VesselType.WingInGround, 0.5, 40.0, 30.0, 2.0),
        [VesselType.Diving] = new(VesselType.Diving, 0.2, 8.0, 40.0),
    };

    // Default profile for unknown types
    private static readonly EmissionProfile DefaultProfile = new(VesselType.OtherType, 0.5, 12.0, 80.0);

    private readonly IVesselStore _vesselStore;

    public EmissionsEstimatorService(IVesselStore vesselStore)
    {
        _vesselStore = vesselStore;
    }

    /// <summary>
    /// Estimate emissions for a single vessel based on its current state.
    /// Returns null if vessel has insufficient data (no position or stationary).
    /// </summary>
    public EmissionEstimate? Estimate(Vessel vessel)
    {
        if (vessel.CurrentPosition is null)
            return null;

        var speed = vessel.CurrentPosition.SpeedOverGround;
        if (speed < MinSpeedKnots)
            return null;

        var profile = Profiles.GetValueOrDefault(vessel.Type, DefaultProfile);
        var fuelPerHour = EstimateFuelConsumption(vessel, speed, profile);

        var co2 = fuelPerHour * Co2FactorPerTonneFuel;
        var sox = fuelPerHour * SoxFactorKgPerTonneFuel;
        var nox = fuelPerHour * NoxFactorKgPerTonneFuel;
        var cii = CalculateCiiRating(vessel, fuelPerHour);

        return new EmissionEstimate
        {
            FuelTonnesPerHour = Math.Round(fuelPerHour, 4),
            Co2TonnesPerHour = Math.Round(co2, 4),
            SoxKgPerHour = Math.Round(sox, 2),
            NoxKgPerHour = Math.Round(nox, 2),
            CiiRating = cii
        };
    }

    /// <summary>
    /// Get fleet-wide emissions summary for all tracked vessels.
    /// </summary>
    public FleetEmissionsSummary GetFleetSummary()
    {
        var vessels = _vesselStore.Vessels.Values.ToList();
        var estimates = new List<(Vessel Vessel, EmissionEstimate Estimate)>();

        foreach (var vessel in vessels)
        {
            var est = Estimate(vessel);
            if (est is not null)
                estimates.Add((vessel, est));
        }

        if (estimates.Count == 0)
        {
            return new FleetEmissionsSummary
            {
                CiiDistribution = new Dictionary<char, int>
                {
                    ['A'] = 0, ['B'] = 0, ['C'] = 0, ['D'] = 0, ['E'] = 0
                }
            };
        }

        var totalCo2 = estimates.Sum(e => e.Estimate.Co2TonnesPerHour);
        var totalFuel = estimates.Sum(e => e.Estimate.FuelTonnesPerHour);

        var highest = estimates.MaxBy(e => e.Estimate.Co2TonnesPerHour);
        var cleanest = estimates.MinBy(e => e.Estimate.Co2TonnesPerHour);

        var ciiDist = new Dictionary<char, int>
        {
            ['A'] = 0, ['B'] = 0, ['C'] = 0, ['D'] = 0, ['E'] = 0
        };
        foreach (var (_, est) in estimates)
        {
            if (ciiDist.ContainsKey(est.CiiRating))
                ciiDist[est.CiiRating]++;
        }

        return new FleetEmissionsSummary
        {
            TotalCo2TonnesPerHour = Math.Round(totalCo2, 2),
            TotalFuelTonnesPerHour = Math.Round(totalFuel, 2),
            VesselsWithEstimates = estimates.Count,
            AverageCo2PerVessel = Math.Round(totalCo2 / estimates.Count, 4),
            HighestEmitterName = highest.Vessel.DisplayName,
            HighestEmitterCo2 = highest.Estimate.Co2TonnesPerHour,
            CleanestVesselName = cleanest.Vessel.DisplayName,
            CleanestVesselCo2 = cleanest.Estimate.Co2TonnesPerHour,
            CiiDistribution = ciiDist
        };
    }

    /// <summary>
    /// Estimate fuel consumption in tonnes/hour using cubic speed-power law
    /// scaled by vessel size relative to the reference profile.
    /// </summary>
    internal static double EstimateFuelConsumption(Vessel vessel, double speedKnots, EmissionProfile profile)
    {
        // Speed ratio: (actual/reference)^exponent — cubic law by default
        var speedRatio = speedKnots / profile.ReferenceSpeedKnots;
        var speedFactor = Math.Pow(speedRatio, profile.SpeedExponent);

        // Size scaling: proportional to vessel length vs reference length
        var vesselLength = vessel.StaticData?.LengthOverall ?? 0;
        var sizeFactor = vesselLength > 0
            ? (double)vesselLength / profile.ReferenceLengthMeters
            : 1.0; // Use reference if no dimensions

        // Clamp size factor to reasonable range
        sizeFactor = Math.Clamp(sizeFactor, 0.1, 5.0);

        return profile.ReferenceFuelTonnesPerHour * speedFactor * sizeFactor;
    }

    /// <summary>
    /// Calculate CII (Carbon Intensity Indicator) rating A-E.
    /// Based on grams CO2 per tonne-mile, using vessel capacity proxy from dimensions.
    /// </summary>
    internal static char CalculateCiiRating(Vessel vessel, double fuelTonnesPerHour)
    {
        // Approximate DWT (deadweight tonnage) from dimensions
        var length = vessel.StaticData?.LengthOverall ?? 0;
        var beam = vessel.StaticData?.Beam ?? 0;
        var draught = vessel.StaticData?.Draught ?? 0;

        // Block coefficient approximation for cargo/tanker vessels
        const double blockCoeff = 0.7;
        var displacementTonnes = length * beam * draught * blockCoeff * 1.025; // seawater density
        var estimatedDwt = displacementTonnes * 0.65; // DWT ~65% of displacement for cargo

        if (estimatedDwt < 10) // Insufficient data for CII
            estimatedDwt = 5000; // Default assumption for rating

        var speed = vessel.CurrentPosition?.SpeedOverGround ?? 0;
        if (speed < MinSpeedKnots)
            return 'C'; // Default when stationary

        // CO2 per tonne-mile: (fuel * CO2_factor) / (DWT * speed)
        var co2PerHour = fuelTonnesPerHour * Co2FactorPerTonneFuel * 1_000_000; // grams
        var tonneNmPerHour = estimatedDwt * speed;
        var attainedCii = co2PerHour / tonneNmPerHour; // g CO2 per tonne-NM

        // CII boundaries (simplified, aligned with IMO 2023 reference lines for bulk carriers)
        // A: <5, B: 5-7.5, C: 7.5-10, D: 10-15, E: >15
        return attainedCii switch
        {
            < 5.0 => 'A',
            < 7.5 => 'B',
            < 10.0 => 'C',
            < 15.0 => 'D',
            _ => 'E'
        };
    }
}
