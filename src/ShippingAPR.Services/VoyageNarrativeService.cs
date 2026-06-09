using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Formatting;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class VoyageNarrativeService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly IPortRepository _portRepository;
    private readonly ILogger<VoyageNarrativeService> _logger;
    private readonly ConcurrentDictionary<int, List<VoyageEvent>> _vesselEvents = new();
    private readonly ConcurrentDictionary<int, double> _lastSpeed = new();
    private readonly ConcurrentDictionary<int, NavigationalStatus> _lastStatus = new();

    private const int MaxEventsPerVessel = 50;
    private const double SpeedChangeThresholdKnots = 3.0;

    public VoyageNarrativeService(
        IVesselStore vesselStore,
        IPortRepository portRepository,
        ILogger<VoyageNarrativeService> logger)
    {
        _vesselStore = vesselStore;
        _portRepository = portRepository;
        _logger = logger;

        _vesselStore.VesselAdded += OnVesselAdded;
        _vesselStore.VesselUpdated += OnVesselUpdated;
        _vesselStore.VesselRemoved += OnVesselRemoved;
    }

    private void OnVesselRemoved(object? sender, Vessel vessel)
    {
        // Clean up tracking data for removed vessels to prevent memory leaks
        _vesselEvents.TryRemove(vessel.Mmsi, out _);
        _lastSpeed.TryRemove(vessel.Mmsi, out _);
        _lastStatus.TryRemove(vessel.Mmsi, out _);
    }

    private void OnVesselAdded(object? sender, Vessel vessel)
    {
        var events = _vesselEvents.GetOrAdd(vessel.Mmsi, _ => new List<VoyageEvent>());
        lock (events)
        {
            events.Add(new VoyageEvent
            {
                Type = VoyageEventType.FirstSeen,
                Timestamp = DateTime.UtcNow,
                Description = $"First detected at {FormatPosition(vessel)}",
                Latitude = vessel.CurrentPosition?.Latitude,
                Longitude = vessel.CurrentPosition?.Longitude,
                SpeedKnots = vessel.CurrentPosition?.SpeedOverGround
            });
        }

        // Initialize baseline speed/status so subsequent updates can detect changes
        if (vessel.CurrentPosition is { } pos)
        {
            _lastSpeed[vessel.Mmsi] = pos.SpeedOverGround;
            _lastStatus[vessel.Mmsi] = pos.Status;
        }
    }

    private void OnVesselUpdated(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is not { } pos) return;

        var events = _vesselEvents.GetOrAdd(vessel.Mmsi, _ => new List<VoyageEvent>());

        lock (events)
        {
            // Check for significant speed change
            if (_lastSpeed.TryGetValue(vessel.Mmsi, out var lastSpeed))
            {
                var diff = Math.Abs(pos.SpeedOverGround - lastSpeed);
                if (diff >= SpeedChangeThresholdKnots)
                {
                    var direction = pos.SpeedOverGround > lastSpeed ? "accelerated" : "decelerated";
                    AddEvent(events, new VoyageEvent
                    {
                        Type = VoyageEventType.SpeedChange,
                        Timestamp = pos.Timestamp,
                        Description = $"{direction.Substring(0, 1).ToUpper()}{direction[1..]} from {lastSpeed:F1} to {pos.SpeedOverGround:F1} kn",
                        Latitude = pos.Latitude,
                        Longitude = pos.Longitude,
                        SpeedKnots = pos.SpeedOverGround
                    });
                }
            }
            _lastSpeed[vessel.Mmsi] = pos.SpeedOverGround;

            // Check for navigational status change
            if (_lastStatus.TryGetValue(vessel.Mmsi, out var lastStatus) && lastStatus != pos.Status)
            {
                var statusDesc = pos.Status switch
                {
                    NavigationalStatus.AtAnchor => "dropped anchor",
                    NavigationalStatus.Moored => "moored",
                    NavigationalStatus.UnderWayUsingEngine => "got underway (engine)",
                    NavigationalStatus.UnderWaySailing => "got underway (sailing)",
                    NavigationalStatus.EngagedInFishing => "began fishing operations",
                    NavigationalStatus.Aground => "reported aground",
                    _ => $"status changed to {pos.Status}"
                };

                AddEvent(events, new VoyageEvent
                {
                    Type = pos.Status == NavigationalStatus.Moored ? VoyageEventType.Moored :
                           pos.Status == NavigationalStatus.AtAnchor ? VoyageEventType.Anchored :
                           VoyageEventType.PositionUpdate,
                    Timestamp = pos.Timestamp,
                    Description = FormatStatusEvent(statusDesc, vessel),
                    Latitude = pos.Latitude,
                    Longitude = pos.Longitude,
                    SpeedKnots = pos.SpeedOverGround
                });
            }
            _lastStatus[vessel.Mmsi] = pos.Status;
        }
    }

    private static string FormatStatusEvent(string action, Vessel vessel)
    {
        var nearPort = "";
        if (vessel.StaticData?.Destination is { Length: > 0 } dest)
            nearPort = $" (destination: {dest})";
        return $"{action}{nearPort}";
    }

    private static void AddEvent(List<VoyageEvent> events, VoyageEvent evt)
    {
        events.Add(evt);
        if (events.Count > MaxEventsPerVessel)
            events.RemoveAt(0);
    }

    public IReadOnlyList<VoyageEvent> GetEvents(int mmsi)
    {
        if (!_vesselEvents.TryGetValue(mmsi, out var events))
            return [];

        lock (events)
        {
            return events.ToList();
        }
    }

    public string GenerateNarrative(int mmsi)
    {
        var vessel = _vesselStore.GetByMmsi(mmsi);
        if (vessel is null) return "Vessel not found.";

        var name = vessel.DisplayName;
        var events = GetEvents(mmsi);
        var pos = vessel.CurrentPosition;
        var eta = vessel.CalculatedEta;

        var parts = new List<string>();

        // Opening
        var typeDesc = vessel.Type != VesselType.Unknown
            ? $"The {FormatType(vessel.Type)} {name}"
            : name;

        if (events.Count > 0)
        {
            var firstSeen = events[0];
            parts.Add($"{typeDesc} was first detected {FormatTimeAgo(firstSeen.Timestamp)}.");
        }
        else
        {
            parts.Add($"{typeDesc} is currently being tracked.");
        }

        // Current state
        if (pos is not null)
        {
            parts.Add($"Currently at {CoordinateFormatter.Format(pos.Latitude, pos.Longitude, 3)} " +
                       $"making {pos.SpeedOverGround:F1} knots on course {pos.CourseOverGround:F0}°.");
        }

        // Destination & ETA
        if (vessel.StaticData?.Destination is { Length: > 0 } dest && eta is not null)
        {
            parts.Add($"Heading for {dest}, {eta.DistanceNauticalMiles:F0} nm away " +
                       $"with ETA {eta.EstimatedArrival:MMM dd HH:mm} UTC.");
        }
        else if (vessel.StaticData?.Destination is { Length: > 0 } d)
        {
            parts.Add($"Destination: {d}.");
        }

        // Notable events
        var notable = events
            .Where(e => e.Type != VoyageEventType.FirstSeen && e.Type != VoyageEventType.PositionUpdate)
            .TakeLast(3);

        foreach (var evt in notable)
        {
            parts.Add($"At {evt.Timestamp:HH:mm} UTC: {evt.Description}.");
        }

        // Dimensions
        if (vessel.StaticData is { LengthOverall: > 0 } sd)
        {
            parts.Add($"Ship dimensions: {sd.LengthOverall}m × {sd.Beam}m.");
        }

        return string.Join(" ", parts);
    }

    private static string FormatType(VesselType type) => type switch
    {
        VesselType.Cargo => "cargo vessel",
        VesselType.Tanker => "tanker",
        VesselType.Passenger => "passenger vessel",
        VesselType.Fishing => "fishing vessel",
        VesselType.Tug => "tug",
        VesselType.Pilot => "pilot vessel",
        VesselType.HighSpeedCraft => "high-speed craft",
        VesselType.Sailing => "sailing vessel",
        VesselType.Military => "military vessel",
        VesselType.PleasureCraft => "pleasure craft",
        VesselType.SearchAndRescue => "search and rescue vessel",
        _ => "vessel"
    };

    private static string FormatPosition(Vessel vessel)
    {
        if (vessel.CurrentPosition is not { } pos) return "unknown position";
        return CoordinateFormatter.Format(pos.Latitude, pos.Longitude, 2);
    }

    private static string FormatTimeAgo(DateTime timestamp)
    {
        var ago = DateTime.UtcNow - timestamp;
        return ago.TotalMinutes < 1 ? "just now" :
               ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes}m ago" :
               ago.TotalHours < 24 ? $"{(int)ago.TotalHours}h ago" :
               $"{(int)ago.TotalDays}d ago";
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselAdded;
        _vesselStore.VesselUpdated -= OnVesselUpdated;
        _vesselStore.VesselRemoved -= OnVesselRemoved;
    }
}
