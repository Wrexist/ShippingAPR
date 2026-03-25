using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Monitors vessel traffic through major maritime chokepoints (Suez, Panama, Malacca, etc.)
/// and produces real-time transit statistics.
/// </summary>
public sealed class ChokepointMonitorService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly ILogger<ChokepointMonitorService> _logger;

    // Vessels currently inside each chokepoint zone
    private readonly ConcurrentDictionary<string, HashSet<int>> _vesselsInZone = new();
    // Transit log per chokepoint
    private readonly ConcurrentDictionary<string, List<ChokepointTransitRecord>> _transitLog = new();
    private readonly object _lock = new();

    private const int MaxTransitRecords = 200;

    public event EventHandler<ChokepointTransitRecord>? TransitDetected;

    /// <summary>
    /// The 7 most important global maritime chokepoints.
    /// </summary>
    public static IReadOnlyList<Chokepoint> Chokepoints { get; } = new List<Chokepoint>
    {
        new("Suez Canal",
            new BoundingBox(29.8, 31.3, 32.2, 32.6),
            30.58, 32.35, 720),

        new("Panama Canal",
            new BoundingBox(8.8, 9.4, -79.95, -79.5),
            9.1, -79.7, 600),

        new("Strait of Malacca",
            new BoundingBox(1.0, 4.5, 99.5, 104.5),
            2.5, 101.5, 720),

        new("Strait of Hormuz",
            new BoundingBox(25.5, 27.0, 55.5, 57.0),
            26.5, 56.3, 120),

        new("Bosphorus",
            new BoundingBox(40.95, 41.25, 28.95, 29.15),
            41.1, 29.05, 90),

        new("Strait of Dover",
            new BoundingBox(50.8, 51.2, 1.0, 1.9),
            51.0, 1.4, 60),

        new("Strait of Gibraltar",
            new BoundingBox(35.8, 36.2, -5.6, -5.3),
            36.0, -5.45, 60)
    };

    public ChokepointMonitorService(IVesselStore vesselStore, ILogger<ChokepointMonitorService> logger)
    {
        _vesselStore = vesselStore;
        _logger = logger;

        foreach (var cp in Chokepoints)
        {
            _vesselsInZone[cp.Name] = new HashSet<int>();
            _transitLog[cp.Name] = new List<ChokepointTransitRecord>();
        }

        _vesselStore.VesselAdded += OnVesselUpdate;
        _vesselStore.VesselUpdated += OnVesselUpdate;
        _vesselStore.StoreCleared += OnStoreCleared;
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is not { } pos) return;

        foreach (var cp in Chokepoints)
        {
            var isInZone = cp.Bounds.Contains(pos.Latitude, pos.Longitude);
            var vesselSet = _vesselsInZone[cp.Name];

            lock (vesselSet)
            {
                var wasInZone = vesselSet.Contains(vessel.Mmsi);

                if (isInZone && !wasInZone)
                {
                    vesselSet.Add(vessel.Mmsi);
                    RecordTransit(cp.Name, vessel, pos.SpeedOverGround);
                }
                else if (!isInZone && wasInZone)
                {
                    vesselSet.Remove(vessel.Mmsi);
                }
            }
        }
    }

    private void RecordTransit(string chokepointName, Vessel vessel, double speed)
    {
        var record = new ChokepointTransitRecord
        {
            Mmsi = vessel.Mmsi,
            VesselName = vessel.DisplayName,
            VesselType = vessel.Type.ToString(),
            SpeedKnots = speed,
            Timestamp = DateTime.UtcNow
        };

        var log = _transitLog[chokepointName];
        lock (log)
        {
            log.Add(record);
            if (log.Count > MaxTransitRecords)
                log.RemoveAt(0);
        }

        TransitDetected?.Invoke(this, record);
        _logger.LogDebug("Chokepoint transit: {Vessel} entered {Chokepoint}", vessel.DisplayName, chokepointName);
    }

    private void OnStoreCleared(object? sender, EventArgs e)
    {
        foreach (var set in _vesselsInZone.Values)
            lock (set) { set.Clear(); }
    }

    public ChokepointStatus GetStatus(string chokepointName)
    {
        var cp = Chokepoints.FirstOrDefault(c => c.Name == chokepointName);
        if (cp is null)
            return new ChokepointStatus { Name = chokepointName };

        var now = DateTime.UtcNow;
        var cutoff24h = now.AddHours(-24);

        var inZone = _vesselsInZone.TryGetValue(chokepointName, out var set)
            ? set.Count
            : 0;

        var log = _transitLog.TryGetValue(chokepointName, out var records)
            ? records
            : new List<ChokepointTransitRecord>();

        List<ChokepointTransitRecord> recentTransits;
        int transits24h;
        double avgSpeed;

        lock (log)
        {
            var recent = log.Where(r => r.Timestamp >= cutoff24h).ToList();
            transits24h = recent.Count;
            avgSpeed = recent.Count > 0 ? recent.Average(r => r.SpeedKnots) : 0;
            recentTransits = recent.TakeLast(10).Reverse().ToList();
        }

        var congestion = (inZone, transits24h) switch
        {
            ( > 20, _) or (_, > 100) => ChokepointCongestion.VeryHigh,
            ( > 10, _) or (_, > 50) => ChokepointCongestion.High,
            ( > 5, _) or (_, > 20) => ChokepointCongestion.Moderate,
            _ => ChokepointCongestion.Low
        };

        return new ChokepointStatus
        {
            Name = chokepointName,
            VesselsInTransit = inZone,
            TransitsLast24h = transits24h,
            AvgSpeedKnots = Math.Round(avgSpeed, 1),
            CongestionLevel = congestion,
            TypicalTransitMinutes = cp.TypicalTransitMinutes,
            CenterLatitude = cp.CenterLatitude,
            CenterLongitude = cp.CenterLongitude,
            RecentTransits = recentTransits
        };
    }

    public IReadOnlyList<ChokepointStatus> GetAllStatuses()
    {
        return Chokepoints.Select(cp => GetStatus(cp.Name)).ToList();
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselUpdate;
        _vesselStore.VesselUpdated -= OnVesselUpdate;
        _vesselStore.StoreCleared -= OnStoreCleared;
    }
}
