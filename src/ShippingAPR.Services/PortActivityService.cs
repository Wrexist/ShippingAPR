using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class PortActivityService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly IPortRepository _portRepository;
    private readonly NotificationService _notificationService;
    private readonly ILogger<PortActivityService> _logger;

    // Track which vessels are "in port" (within radius) per port
    private readonly ConcurrentDictionary<string, HashSet<int>> _vesselsInPort = new();
    private readonly ConcurrentDictionary<string, List<PortActivityRecord>> _activityLog = new();

    // Cache ports list to avoid re-fetching on every vessel update
    private IReadOnlyList<Port>? _cachedPorts;

    private const double PortRadiusNm = 3.0; // nautical miles
    private const int MaxActivityRecords = 200;

    public string? SelectedPortName { get; set; }

    public event EventHandler<PortActivityRecord>? ActivityRecorded;

    public PortActivityService(
        IVesselStore vesselStore,
        IPortRepository portRepository,
        NotificationService notificationService,
        ILogger<PortActivityService> logger)
    {
        _vesselStore = vesselStore;
        _portRepository = portRepository;
        _notificationService = notificationService;
        _logger = logger;

        _vesselStore.VesselAdded += OnVesselUpdate;
        _vesselStore.VesselUpdated += OnVesselUpdate;
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is not { } pos) return;

        // Cache ports list to avoid O(n) allocation on every vessel update
        _cachedPorts ??= _portRepository.GetAll();
        foreach (var port in _cachedPorts)
        {
            var distNm = HaversineCalculator.DistanceInNauticalMiles(
                pos.Latitude, pos.Longitude, port.Latitude, port.Longitude);
            if (distNm > PortRadiusNm * 3) continue; // Skip distant ports entirely

            var vesselSet = _vesselsInPort.GetOrAdd(port.Name, _ => new HashSet<int>());
            bool wasInPort, isInPort = distNm <= PortRadiusNm;

            lock (vesselSet)
            {
                wasInPort = vesselSet.Contains(vessel.Mmsi);

                if (isInPort && !wasInPort)
                {
                    // Arrival
                    vesselSet.Add(vessel.Mmsi);
                    RecordActivity(port.Name, vessel, PortActivityType.Arrival, pos.SpeedOverGround);
                }
                else if (!isInPort && wasInPort)
                {
                    // Departure
                    vesselSet.Remove(vessel.Mmsi);
                    RecordActivity(port.Name, vessel, PortActivityType.Departure, pos.SpeedOverGround);
                }
            }
        }
    }

    private void RecordActivity(string portName, Vessel vessel, PortActivityType type, double speed)
    {
        var record = new PortActivityRecord
        {
            PortName = portName,
            Mmsi = vessel.Mmsi,
            VesselName = vessel.DisplayName,
            ActivityType = type,
            Timestamp = DateTime.UtcNow,
            SpeedKnots = speed
        };

        var log = _activityLog.GetOrAdd(portName, _ => new List<PortActivityRecord>());
        lock (log)
        {
            log.Add(record);
            if (log.Count > MaxActivityRecords)
                log.RemoveAt(0);
        }

        if (portName == SelectedPortName)
        {
            var action = type == PortActivityType.Arrival ? "arrived at" : "departed from";
            _notificationService.Publish(new NotificationMessage
            {
                Title = $"Port Activity: {portName}",
                Body = $"{vessel.DisplayName} {action} {portName} at {speed:F1} kn",
                Type = type == PortActivityType.Arrival ? NotificationType.VesselEntered : NotificationType.VesselLeft
            });
        }

        ActivityRecorded?.Invoke(this, record);
        _logger.LogDebug("Port activity: {Vessel} {Type} {Port}", vessel.DisplayName, type, portName);
    }

    public PortCongestionSnapshot GetCongestion(string portName)
    {
        var now = DateTime.UtcNow;
        var cutoff24h = now.AddHours(-24);

        var inPort = _vesselsInPort.TryGetValue(portName, out var set)
            ? set.Count
            : 0;

        var log = _activityLog.TryGetValue(portName, out var records)
            ? records
            : new List<PortActivityRecord>();

        List<PortActivityRecord> recentRecords;
        int arrivals, departures;
        var hourly = new Dictionary<int, int>();

        lock (log)
        {
            var recent = log.Where(r => r.Timestamp >= cutoff24h).ToList();
            arrivals = recent.Count(r => r.ActivityType == PortActivityType.Arrival);
            departures = recent.Count(r => r.ActivityType == PortActivityType.Departure);
            recentRecords = recent.TakeLast(20).Reverse().ToList();

            foreach (var r in recent)
            {
                var hour = r.Timestamp.Hour;
                hourly[hour] = hourly.GetValueOrDefault(hour) + 1;
            }
        }

        // Congestion score: simple heuristic based on vessels in port vs typical traffic
        var totalTraffic = arrivals + departures;
        var congestionScore = totalTraffic > 0
            ? Math.Min(1.0, inPort / Math.Max(totalTraffic * 0.3, 5.0))
            : 0.0;

        return new PortCongestionSnapshot
        {
            PortName = portName,
            VesselsInPort = inPort,
            ArrivalsLast24h = arrivals,
            DeparturesLast24h = departures,
            CongestionScore = congestionScore,
            RecentActivity = recentRecords,
            HourlyTraffic = hourly
        };
    }

    public IReadOnlyList<string> GetActivePortNames()
    {
        return _vesselsInPort
            .Where(kvp => kvp.Value.Count > 0)
            .Select(kvp => kvp.Key)
            .OrderBy(n => n)
            .ToList();
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselUpdate;
        _vesselStore.VesselUpdated -= OnVesselUpdate;
    }
}
