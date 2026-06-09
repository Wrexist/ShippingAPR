using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Enums;
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
    /// <summary>A vessel staying within this radius counts as not having left the spot.</summary>
    private const double LoiterRadiusNm = 0.5;
    /// <summary>Time within the loiter radius before it is flagged.</summary>
    private static readonly TimeSpan LoiterDuration = TimeSpan.FromMinutes(60);
    /// <summary>Don't flag loitering within this distance of a known port/anchorage.</summary>
    private const double PortExclusionNm = 10.0;
    private const int MaxRecent = 200;

    private readonly IVesselStore _vesselStore;
    private readonly NotificationService _notificationService;
    private readonly IPortRepository _portRepository;
    private readonly ILogger<AnomalyDetectionService> _logger;

    private readonly ConcurrentDictionary<int, (double Lat, double Lon, double Sog, DateTime Ts)> _last = new();
    private readonly ConcurrentDictionary<int, (double Lat, double Lon, DateTime SinceTs, bool Alerted)> _loiterAnchor = new();
    private readonly ConcurrentQueue<Anomaly> _recent = new();

    private readonly EventHandler<Vessel> _onVesselUpdated;
    private readonly EventHandler<Vessel> _onVesselRemoved;

    public event EventHandler<Anomaly>? AnomalyDetected;
    public IReadOnlyList<Anomaly> RecentAnomalies => _recent.ToArray();

    public AnomalyDetectionService(
        IVesselStore vesselStore,
        NotificationService notificationService,
        IPortRepository portRepository,
        ILogger<AnomalyDetectionService> logger)
    {
        _vesselStore = vesselStore;
        _notificationService = notificationService;
        _portRepository = portRepository;
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

        var jump = CheckPositionJump(vessel.Mmsi, pos.Latitude, pos.Longitude, pos.SpeedOverGround, pos.Timestamp);
        if (jump is not null) Record(jump);

        var loiter = CheckLoitering(vessel.Mmsi, pos.Latitude, pos.Longitude, pos.Status, pos.Timestamp);
        if (loiter is not null) Record(loiter);
    }

    internal void OnVesselRemoved(int mmsi)
    {
        _loiterAnchor.TryRemove(mmsi, out _);
        var anomaly = CheckAisGap(mmsi, DateTime.UtcNow);
        if (anomaly is not null)
            Record(anomaly);
    }

    /// <summary>
    /// Returns a loitering anomaly if the vessel has stayed within a small radius for
    /// longer than the loiter duration, excluding vessels reporting at-anchor/moored and
    /// those near a known port/anchorage. Resets the anchor when the vessel moves away.
    /// </summary>
    internal Anomaly? CheckLoitering(int mmsi, double lat, double lon, NavigationalStatus status, DateTime ts)
    {
        // A vessel explicitly anchored/moored isn't loitering anomalously.
        if (status is NavigationalStatus.AtAnchor or NavigationalStatus.Moored)
        {
            _loiterAnchor.TryRemove(mmsi, out _);
            return null;
        }

        if (!_loiterAnchor.TryGetValue(mmsi, out var anchor))
        {
            _loiterAnchor[mmsi] = (lat, lon, ts, false);
            return null;
        }

        var dist = HaversineCalculator.DistanceInNauticalMiles(anchor.Lat, anchor.Lon, lat, lon);
        if (dist > LoiterRadiusNm)
        {
            _loiterAnchor[mmsi] = (lat, lon, ts, false); // moved to a new spot
            return null;
        }

        if (anchor.Alerted || ts - anchor.SinceTs < LoiterDuration)
            return null;

        // Normal port/anchorage areas are expected — don't flag them.
        if (_portRepository.FindNearest(lat, lon, PortExclusionNm) is not null)
            return null;

        _loiterAnchor[mmsi] = (anchor.Lat, anchor.Lon, anchor.SinceTs, true);
        return new Anomaly(mmsi, AnomalyType.Loitering,
            $"Loitering within {LoiterRadiusNm:F1} NM for over {LoiterDuration.TotalMinutes:F0} min",
            lat, lon, ts);
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
