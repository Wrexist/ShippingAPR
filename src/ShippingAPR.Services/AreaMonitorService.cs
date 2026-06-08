using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class VesselAreaEvent
{
    public required Vessel Vessel { get; init; }
    public required bool Entered { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? ZoneName { get; init; }
}

public sealed class VesselAreaNotification : ValueChangedMessage<VesselAreaEvent>
{
    public VesselAreaNotification(VesselAreaEvent value) : base(value) { }
}

public sealed class AreaMonitorService : IDisposable
{
    private enum Transition { None, Entered, Left }

    /// <summary>Fraction of each zone dimension used as the hysteresis deadband.</summary>
    private const double HysteresisFraction = 0.05;
    /// <summary>Floor for the deadband (~500 m) so very small zones still resist jitter.</summary>
    private const double MinMarginDegrees = 0.005;

    private readonly IVesselStore _vesselStore;
    private readonly ILogger<AreaMonitorService> _logger;
    private readonly EventHandler _onStoreCleared;

    // Legacy single-area support. Value = whether the vessel is currently considered inside.
    private readonly ConcurrentDictionary<int, bool> _previousState = new();
    private BoundingBox? _monitoredArea;

    // Multi-zone geofences
    private readonly ConcurrentDictionary<string, GeofenceZone> _geofences = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _geofenceStates = new();

    public event EventHandler<VesselAreaEvent>? VesselAreaChanged;
    public event EventHandler<VesselGeofenceEvent>? GeofenceTriggered;
    public event EventHandler? GeofencesChanged;

    public IReadOnlyCollection<GeofenceZone> Geofences => _geofences.Values.ToList();

    public AreaMonitorService(
        IVesselStore vesselStore,
        ILogger<AreaMonitorService> logger)
    {
        _vesselStore = vesselStore;
        _logger = logger;

        _onStoreCleared = (_, _) =>
        {
            _previousState.Clear();
            foreach (var state in _geofenceStates.Values)
                state.Clear();
        };
        _vesselStore.VesselAdded += OnVesselUpdated;
        _vesselStore.VesselUpdated += OnVesselUpdated;
        _vesselStore.StoreCleared += _onStoreCleared;
    }

    public void SetMonitoredArea(BoundingBox? area)
    {
        _monitoredArea = area;
        _previousState.Clear();
    }

    public void AddGeofence(GeofenceZone zone)
    {
        _geofences[zone.Name] = zone;
        _geofenceStates[zone.Name] = new ConcurrentDictionary<int, bool>();
        _logger.LogInformation("Added geofence zone: {Name}", zone.Name);
        GeofencesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveGeofence(string name)
    {
        _geofences.TryRemove(name, out _);
        _geofenceStates.TryRemove(name, out _);
        _logger.LogInformation("Removed geofence zone: {Name}", name);
        GeofencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVesselUpdated(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is null) return;

        var lat = vessel.CurrentPosition.Latitude;
        var lon = vessel.CurrentPosition.Longitude;

        // Legacy single-area monitoring
        if (_monitoredArea is not null)
        {
            var (latM, lonM) = Margin(_monitoredArea);
            var transition = EvaluateTransition(_previousState, vessel.Mmsi,
                _monitoredArea.Contains(lat, lon),
                _monitoredArea.ContainsWithMargin(lat, lon, latM, lonM));
            if (transition != Transition.None)
                RaiseAreaEvent(vessel, transition == Transition.Entered, null);
        }

        // Multi-zone geofence monitoring
        foreach (var (name, zone) in _geofences)
        {
            if (!_geofenceStates.TryGetValue(name, out var states)) continue;

            var (latM, lonM) = Margin(zone.Bounds);
            var transition = EvaluateTransition(states, vessel.Mmsi,
                zone.Contains(lat, lon),
                zone.ContainsWithMargin(lat, lon, latM, lonM));
            if (transition == Transition.Entered && zone.AlertOnEntry)
            {
                RaiseGeofenceEvent(vessel, zone, entered: true);
                RaiseAreaEvent(vessel, entered: true, name);
            }
            else if (transition == Transition.Left && zone.AlertOnExit)
            {
                RaiseGeofenceEvent(vessel, zone, entered: false);
                RaiseAreaEvent(vessel, entered: false, name);
            }
        }
    }

    /// <summary>The hysteresis deadband margins (lat, lon) for a zone's bounding box.</summary>
    private static (double Lat, double Lon) Margin(BoundingBox box) =>
        (Math.Max(MinMarginDegrees, HysteresisFraction * (box.MaxLatitude - box.MinLatitude)),
         Math.Max(MinMarginDegrees, HysteresisFraction * (box.MaxLongitude - box.MinLongitude)));

    /// <summary>
    /// Updates per-vessel zone state and returns whether this update is an enter/leave
    /// transition. The first observation only records a baseline (never alerts). A vessel
    /// is considered to have entered once it is inside the zone, and to have left only once
    /// it is also outside the zone grown by a hysteresis margin — the deadband in between
    /// stops a vessel lingering on the boundary from flapping alerts, while a genuine
    /// crossing (even a brief one) still fires.
    /// </summary>
    private static Transition EvaluateTransition(
        ConcurrentDictionary<int, bool> states, int mmsi, bool isInside, bool insideWithMargin)
    {
        var transition = Transition.None;
        states.AddOrUpdate(mmsi,
            // First time we see this vessel for this zone: record a baseline, never alert.
            _ => isInside,
            (_, wasInside) =>
            {
                if (!wasInside && isInside)
                {
                    transition = Transition.Entered;
                    return true;
                }
                if (wasInside && !insideWithMargin)
                {
                    transition = Transition.Left;
                    return false;
                }
                // Inside the deadband or no change — keep the current state.
                return wasInside;
            });

        return transition;
    }

    private void RaiseAreaEvent(Vessel vessel, bool entered, string? zoneName)
    {
        var evt = new VesselAreaEvent
        {
            Vessel = vessel,
            Entered = entered,
            Timestamp = DateTime.UtcNow,
            ZoneName = zoneName
        };
        _logger.LogInformation("Vessel {Name} (MMSI {Mmsi}) {Direction} monitored area {Zone}",
            vessel.DisplayName, vessel.Mmsi, entered ? "entered" : "left", zoneName);
        VesselAreaChanged?.Invoke(this, evt);
        WeakReferenceMessenger.Default.Send(new VesselAreaNotification(evt));
    }

    private void RaiseGeofenceEvent(Vessel vessel, GeofenceZone zone, bool entered)
    {
        var geoEvt = new VesselGeofenceEvent
        {
            Vessel = vessel,
            Zone = zone,
            Entered = entered,
            Timestamp = DateTime.UtcNow
        };
        _logger.LogInformation("Vessel {Name} {Direction} geofence '{Zone}'",
            vessel.DisplayName, entered ? "entered" : "left", zone.Name);
        GeofenceTriggered?.Invoke(this, geoEvt);
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselUpdated;
        _vesselStore.VesselUpdated -= OnVesselUpdated;
        _vesselStore.StoreCleared -= _onStoreCleared;
    }
}
