namespace ShippingAPR.Core.Diagnostics;

/// <summary>Liveness of the incoming AIS data stream as seen by the UI.</summary>
public enum DataFreshnessState
{
    /// <summary>Not connected — freshness is not meaningful.</summary>
    Idle,
    /// <summary>Connected but no data has arrived yet.</summary>
    Waiting,
    /// <summary>Connected and data arrived within the stale threshold.</summary>
    Live,
    /// <summary>Connected but no data has arrived for longer than the threshold (stream likely stalled).</summary>
    Stale
}

/// <summary>
/// Pure helpers for deciding whether the AIS stream is live or silently stalled,
/// so the UI can always tell the user "live" from "last known" rather than showing
/// a frozen but "Connected" map. Kept dependency-free and unit-tested.
/// </summary>
public static class DataFreshness
{
    public static DataFreshnessState Evaluate(
        bool isConnected, DateTime? lastDataUtc, DateTime nowUtc, TimeSpan staleThreshold)
    {
        if (!isConnected) return DataFreshnessState.Idle;
        if (lastDataUtc is null) return DataFreshnessState.Waiting;
        return nowUtc - lastDataUtc.Value > staleThreshold
            ? DataFreshnessState.Stale
            : DataFreshnessState.Live;
    }

    /// <summary>Compact human description of an age, e.g. "5s", "3m", "2h".</summary>
    public static string DescribeAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds}s";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalHours}h";
    }
}
