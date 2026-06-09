using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.Persistence;

namespace ShippingAPR.Services;

/// <summary>
/// Periodically flushes the latest position of each updated vessel to the durable
/// <see cref="ITrackHistoryStore"/> (one sampled point per vessel per flush interval),
/// and purges history beyond the retention window. This builds the persistent track
/// history that powers future playback and analytics.
/// </summary>
public sealed class TrackPersistenceService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(6);

    private readonly IVesselStore _vesselStore;
    private readonly ITrackHistoryStore _historyStore;
    private readonly ILogger<TrackPersistenceService> _logger;
    private readonly int _retentionDays;
    private readonly ConcurrentDictionary<int, TrackPoint> _pending = new();
    private readonly EventHandler<Vessel> _onVesselChanged;
    private DateTime _lastPurgeUtc = DateTime.MinValue;

    public TrackPersistenceService(
        IVesselStore vesselStore,
        ITrackHistoryStore historyStore,
        IOptions<TrackHistoryOptions> options,
        ILogger<TrackPersistenceService> logger)
    {
        _vesselStore = vesselStore;
        _historyStore = historyStore;
        _logger = logger;
        _retentionDays = Math.Max(1, options.Value.RetentionDays);

        _onVesselChanged = OnVesselChanged;
        _vesselStore.VesselAdded += _onVesselChanged;
        _vesselStore.VesselUpdated += _onVesselChanged;
    }

    private void OnVesselChanged(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is { } pos)
            _pending[vessel.Mmsi] = new TrackPoint(pos.Latitude, pos.Longitude, pos.SpeedOverGround, pos.Timestamp);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
                }
                catch (OperationCanceledException) { break; }

                await FlushAsync(stoppingToken);
                await MaybePurgeAsync(stoppingToken);
            }
        }
        finally
        {
            _vesselStore.VesselAdded -= _onVesselChanged;
            _vesselStore.VesselUpdated -= _onVesselChanged;
            // Best-effort final flush on shutdown.
            await FlushAsync(CancellationToken.None);
        }
    }

    internal async Task FlushAsync(CancellationToken ct)
    {
        foreach (var mmsi in _pending.Keys.ToArray())
        {
            if (ct.IsCancellationRequested) break;
            if (!_pending.TryRemove(mmsi, out var point)) continue;
            try
            {
                await _historyStore.AddPointAsync(mmsi, point, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to persist track point for MMSI {Mmsi}", mmsi);
            }
        }
    }

    private async Task MaybePurgeAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastPurgeUtc < PurgeInterval) return;
        _lastPurgeUtc = DateTime.UtcNow;
        try
        {
            var removed = await _historyStore.PurgeOlderThanAsync(DateTime.UtcNow.AddDays(-_retentionDays), ct);
            if (removed > 0)
                _logger.LogInformation("Purged {Count} track points older than {Days} days", removed, _retentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Track history purge failed");
        }
    }
}
