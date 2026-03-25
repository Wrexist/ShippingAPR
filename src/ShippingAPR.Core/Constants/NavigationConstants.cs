namespace ShippingAPR.Core.Constants;

/// <summary>
/// Shared navigation and AIS protocol constants used across calculation modules.
/// Centralizes magic numbers to avoid inconsistencies between calculators.
/// </summary>
public static class NavigationConstants
{
    // Speed thresholds (knots)
    /// <summary>Minimum speed to consider a vessel "moving" for CPA and ETA calculations.</summary>
    public const double MinSpeedKnots = 0.5;

    /// <summary>Below this speed, vessel is considered stationary for ETA (no estimate generated).</summary>
    public const double StationaryThresholdKnots = 0.3;

    /// <summary>Upper bound on plausible vessel speed — anything above is treated as invalid AIS data.</summary>
    public const double MaxPlausibleSpeedKnots = 50.0;

    // Distance thresholds (nautical miles)
    /// <summary>Below this distance, vessel is considered "arrived" at destination.</summary>
    public const double MinDistanceNm = 0.1;

    /// <summary>Approximate nautical miles per degree of latitude.</summary>
    public const double NauticalMilesPerDegree = 60.0;

    // ETA calculation
    /// <summary>Floor for cosine of course deviation — prevents division-by-zero when vessel moves perpendicular.</summary>
    public const double CosineDeviationFloor = 0.1;

    // AIS protocol
    /// <summary>AIS true heading value indicating "not available" (field value 511).</summary>
    public const int TrueHeadingNotAvailable = 511;

    /// <summary>AIS draught is reported in 1/10th meters.</summary>
    public const double DraughtDivisor = 10.0;

    /// <summary>Divisor to extract MID (Maritime Identification Digits) from MMSI.</summary>
    public const int MmsiToMidDivisor = 1_000_000;

    /// <summary>Maximum valid AIS navigational status value.</summary>
    public const int MaxNavigationalStatus = 15;
}
