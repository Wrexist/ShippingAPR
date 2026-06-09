namespace ShippingAPR.Core.Validation;

/// <summary>
/// Validation helpers for MMSI (Maritime Mobile Service Identity) numbers.
/// An MMSI is a 9-digit identifier; the valid range therefore spans the full
/// 100000000–999999999 space. This intentionally includes the 8xx/9xx blocks
/// (e.g. 970xxxxxx AIS-SART, 972xxxxxx MOB, 974xxxxxx EPIRB-AIS, 99xxxxxxx
/// aids-to-navigation) which a 7xx upper bound would wrongly reject.
/// </summary>
public static class MmsiValidator
{
    /// <summary>Smallest valid 9-digit MMSI.</summary>
    public const int MinValue = 100_000_000;

    /// <summary>Largest valid 9-digit MMSI.</summary>
    public const int MaxValue = 999_999_999;

    /// <summary>True when <paramref name="mmsi"/> is a syntactically valid 9-digit MMSI.</summary>
    public static bool IsValid(int mmsi) => mmsi is >= MinValue and <= MaxValue;
}
