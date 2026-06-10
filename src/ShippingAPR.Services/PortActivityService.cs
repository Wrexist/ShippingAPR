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

    // Spatial index: bucket ports by 1-degree grid cells for O(1) average lookup
    private Dictionary<(int latBucket, int lonBucket), List<Port>>? _portGrid;
    private readonly object _portGridLock = new();

    private const double PortRadiusNm = 3.0; // arrival radius (nautical miles)
    // A vessel only counts as departed once it is beyond this larger radius, so a vessel
    // manoeuvring on the 3 NM boundary doesn't flap arrival/departure pairs.
    private const double DepartureRadiusNm = 4.0;
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
        _vesselStore.VesselRemoved += OnVesselRemoved;
    }

    private IEnumerable<Port> GetNearbyPorts(double lat, double lon)
    {
        if (_portGrid is null)
        {
            lock (_portGridLock)
            {
                if (_portGrid is null)
                {
                    _cachedPorts ??= _portRepository.GetAll();
                    var grid = new Dictionary<(int, int), List<Port>>();
                    foreach (var p in _cachedPorts)
                    {
                        var key = ((int)Math.Floor(p.Latitude), (int)Math.Floor(p.Longitude));
                        if (!grid.TryGetValue(key, out var list))
                        {
                            list = [];
                            grid[key] = list;
                        }
                        list.Add(p);
                    }
                    _portGrid = grid;
                }
            }
        }

        var baseLat = (int)Math.Floor(lat);
        var baseLon = (int)Math.Floor(lon);

        // Check the vessel's cell and all 8 neighbors (ports near cell boundaries)
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (_portGrid.TryGetValue((baseLat + dy, baseLon + dx), out var ports))
                {
                    foreach (var port in ports)
                        yield return port;
                }
            }
        }
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is not { } pos) return;

        foreach (var port in GetNearbyPorts(pos.Latitude, pos.Longitude))
        {
            var distNm = HaversineCalculator.DistanceInNauticalMiles(
                pos.Latitude, pos.Longitude, port.Latitude, port.Longitude);
            if (distNm > PortRadiusNm * 3)
            {
                // Far outside the port — but if we still had this vessel marked
                // in-port (sparse updates can jump straight past the departure
                // band), record the departure instead of leaving it stranded.
                if (_vesselsInPort.TryGetValue(port.Name, out var staleSet))
                {
                    bool wasStranded;
                    lock (staleSet) { wasStranded = staleSet.Remove(vessel.Mmsi); }
                    if (wasStranded)
                        RecordActivity(port.Name, vessel, PortActivityType.Departure, pos.SpeedOverGround);
                }
                continue;
            }

            var vesselSet = _vesselsInPort.GetOrAdd(port.Name, _ => new HashSet<int>());
            bool arrived = false, departed = false;

            lock (vesselSet)
            {
                var wasInPort = vesselSet.Contains(vessel.Mmsi);

                if (!wasInPort && distNm <= PortRadiusNm)
                {
                    vesselSet.Add(vessel.Mmsi);
                    arrived = true;
                }
                else if (wasInPort && distNm > DepartureRadiusNm)
                {
                    // Must clear the larger departure radius — the gap to the arrival
                    // radius is a deadband that absorbs boundary jitter.
                    vesselSet.Remove(vessel.Mmsi);
                    departed = true;
                }
            }

            // Record outside the lock to avoid holding it across event handlers.
            if (arrived)
                RecordActivity(port.Name, vessel, PortActivityType.Arrival, pos.SpeedOverGround);
            else if (departed)
                RecordActivity(port.Name, vessel, PortActivityType.Departure, pos.SpeedOverGround);
        }
    }

    private void OnVesselRemoved(object? sender, Vessel vessel)
    {
        // A vessel that dropped out of the feed should no longer count as in port.
        foreach (var set in _vesselsInPort.Values)
            lock (set) { set.Remove(vessel.Mmsi); }
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
        _vesselStore.VesselRemoved -= OnVesselRemoved;
    }
}
