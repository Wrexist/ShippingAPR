using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Detects behavioural anomalies that are core to maritime surveillance:
/// position jumps (possible AIS spoofing) and AIS gaps ("dark ships" — a moving
/// vessel that stops transmitting). Emits anomalies as events and notifications.
/// </summary>
public sealed class AnomalyDetectionService : IDisposable
{
    /// <summary>Implied speed (kn) above which a position change is treated as a jump/spoof.</summary>
    private const double MaxImpliedSpeedKnots = 80.0;
    /// <summary>Minimum jump distance (NM) to flag, so GPS jitter at short intervals is ignored.</summary>
    private const double MinJumpDistanceNm = 2.0;
    /// <summary>A vessel was "moving" (and so a sudden silence is notable) above this speed.</summary>
    private const double MovingThresholdKnots = 3.0;
    private const int MaxRecent = 200;

    private readonly IVesselStore _vesselStore;
    private readonly NotificationService _notificationService;
    private readonly ILogger<AnomalyDetectionService> _logger;

    private readonly ConcurrentDictionary<int, (double Lat, double Lon, double Sog, DateTime Ts)> _last = new();
    private readonly ConcurrentQueue<Anomaly> _recent = new();

    private readonly EventHandler<Vessel> _onVesselUpdated;
    private readonly EventHandler<Vessel> _onVesselRemoved;

    public event EventHandler<Anomaly>? AnomalyDetected;
    public IReadOnlyList<Anomaly> RecentAnomalies => _recent.ToArray();

    public AnomalyDetectionService(
        IVesselStore vesselStore,
        NotificationService notificationService,
        ILogger<AnomalyDetectionService> logger)
    {
        _vesselStore = vesselStore;
        _notificationService = notificationService;
        _logger = logger;

        _onVesselUpdated = (_, v) => OnVesselUpdate(v);
        _onVesselRemoved = (_, v) => OnVesselRemoved(v.Mmsi);
        _vesselStore.VesselUpdated += _onVesselUpdated;
        _vesselStore.VesselAdded += _onVesselUpdated;
        _vesselStore.VesselRemoved += _onVesselRemoved;
    }

    internal void OnVesselUpdate(Vessel vessel)
    {
        if (vessel.CurrentPosition is not { } pos) return;
        var anomaly = CheckPositionJump(vessel.Mmsi, pos.Latitude, pos.Longitude, pos.SpeedOverGround, pos.Timestamp);
        if (anomaly is not null)
            Record(anomaly);
    }

    internal void OnVesselRemoved(int mmsi)
    {
        var anomaly = CheckAisGap(mmsi, DateTime.UtcNow);
        if (anomaly is not null)
            Record(anomaly);
    }

    /// <summary>
    /// Returns a position-jump anomaly if the move since the last fix implies an
    /// impossible speed, then records the new fix as the latest known position.
    /// </summary>
    internal Anomaly? CheckPositionJump(int mmsi, double lat, double lon, double sog, DateTime ts)
    {
        Anomaly? anomaly = null;
        if (_last.TryGetValue(mmsi, out var prev))
        {
            var dtHours = (ts - prev.Ts).TotalHours;
            if (dtHours > 0)
            {
                var distNm = HaversineCalculator.DistanceInNauticalMiles(prev.Lat, prev.Lon, lat, lon);
                var impliedSpeed = distNm / dtHours;
                if (distNm >= MinJumpDistanceNm && impliedSpeed > MaxImpliedSpeedKnots)
                {
                    anomaly = new Anomaly(mmsi, AnomalyType.PositionJump,
                        $"Position jumped {distNm:F0} NM in {dtHours * 60:F0} min (implied {impliedSpeed:F0} kn)",
                        lat, lon, ts);
                }
            }
        }
        _last[mmsi] = (lat, lon, sog, ts);
        return anomaly;
    }

    /// <summary>
    /// Returns an AIS-gap anomaly if the vessel was moving when it stopped transmitting,
    /// and clears its tracked state.
    /// </summary>
    internal Anomaly? CheckAisGap(int mmsi, DateTime nowUtc)
    {
        if (_last.TryRemove(mmsi, out var prev) && prev.Sog >= MovingThresholdKnots)
        {
            return new Anomaly(mmsi, AnomalyType.AisGap,
                $"Moving vessel ({prev.Sog:F1} kn) stopped transmitting",
                prev.Lat, prev.Lon, nowUtc);
        }
        return null;
    }

    private void Record(Anomaly anomaly)
    {
        _recent.Enqueue(anomaly);
        while (_recent.Count > MaxRecent && _recent.TryDequeue(out _)) { }

        _logger.LogInformation("Anomaly {Type} for MMSI {Mmsi}: {Description}",
            anomaly.Type, anomaly.Mmsi, anomaly.Description);

        _notificationService.Publish(new NotificationMessage
        {
            Title = $"Anomaly: {anomaly.Type}",
            Body = $"MMSI {anomaly.Mmsi} — {anomaly.Description}",
            Type = NotificationType.Warning
        });

        AnomalyDetected?.Invoke(this, anomaly);
    }

    public void Dispose()
    {
        _vesselStore.VesselUpdated -= _onVesselUpdated;
        _vesselStore.VesselAdded -= _onVesselUpdated;
        _vesselStore.VesselRemoved -= _onVesselRemoved;
    }
}
