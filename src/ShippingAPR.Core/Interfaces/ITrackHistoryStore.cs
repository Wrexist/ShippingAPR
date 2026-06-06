using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

/// <summary>
/// Durable storage for vessel track points, surviving application restarts.
/// This is the foundation for historical playback, density-from-history and
/// time-series analytics that the in-memory ring buffer cannot provide.
/// </summary>
public interface ITrackHistoryStore
{
    /// <summary>Persists a single track point for a vessel.</summary>
    Task AddPointAsync(int mmsi, TrackPoint point, CancellationToken ct = default);

    /// <summary>Persists multiple track points for a vessel in one transaction.</summary>
    Task AddPointsAsync(int mmsi, IEnumerable<TrackPoint> points, CancellationToken ct = default);

    /// <summary>Returns a vessel's stored track points (ascending by time) since the given UTC time.</summary>
    Task<IReadOnlyList<TrackPoint>> GetTrackAsync(int mmsi, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Deletes track points older than the cutoff; returns the number removed.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
