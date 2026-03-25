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
    private readonly IVesselStore _vesselStore;
    private readonly ILogger<AreaMonitorService> _logger;
    private readonly EventHandler _onStoreCleared;

    // Legacy single-area support
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
        _monitoredArea = area ?? throw new ArgumentNullException(nameof(area));
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
            CheckAreaTransition(vessel, lat, lon, _monitoredArea, _previousState, null);
        }

        // Multi-zone geofence monitoring
        foreach (var (name, zone) in _geofences)
        {
            if (!_geofenceStates.TryGetValue(name, out var states)) continue;

            var isInside = zone.Bounds.Contains(lat, lon);
            var wasInside = false;
            states.AddOrUpdate(vessel.Mmsi, isInside, (_, old) => { wasInside = old; return isInside; });

            if (isInside && !wasInside && zone.AlertOnEntry)
            {
                var geoEvt = new VesselGeofenceEvent
                {
                    Vessel = vessel,
                    Zone = zone,
                    Entered = true,
                    Timestamp = DateTime.UtcNow
                };
                _logger.LogInformation("Vessel {Name} entered geofence '{Zone}'", vessel.DisplayName, name);
                GeofenceTriggered?.Invoke(this, geoEvt);

                // Also fire legacy event for notification compatibility
                var areaEvt = new VesselAreaEvent
                {
                    Vessel = vessel,
                    Entered = true,
                    Timestamp = DateTime.UtcNow,
                    ZoneName = name
                };
                VesselAreaChanged?.Invoke(this, areaEvt);
                WeakReferenceMessenger.Default.Send(new VesselAreaNotification(areaEvt));
            }
            else if (!isInside && wasInside && zone.AlertOnExit)
            {
                var geoEvt = new VesselGeofenceEvent
                {
                    Vessel = vessel,
                    Zone = zone,
                    Entered = false,
                    Timestamp = DateTime.UtcNow
                };
                _logger.LogInformation("Vessel {Name} left geofence '{Zone}'", vessel.DisplayName, name);
                GeofenceTriggered?.Invoke(this, geoEvt);

                var areaEvt = new VesselAreaEvent
                {
                    Vessel = vessel,
                    Entered = false,
                    Timestamp = DateTime.UtcNow,
                    ZoneName = name
                };
                VesselAreaChanged?.Invoke(this, areaEvt);
                WeakReferenceMessenger.Default.Send(new VesselAreaNotification(areaEvt));
            }
        }
    }

    private void CheckAreaTransition(Vessel vessel, double lat, double lon, BoundingBox area,
        ConcurrentDictionary<int, bool> state, string? zoneName)
    {
        var isInside = area.Contains(lat, lon);
        var wasInside = false;
        state.AddOrUpdate(vessel.Mmsi, isInside, (_, old) => { wasInside = old; return isInside; });

        if (isInside && !wasInside)
        {
            var evt = new VesselAreaEvent
            {
                Vessel = vessel,
                Entered = true,
                Timestamp = DateTime.UtcNow,
                ZoneName = zoneName
            };
            _logger.LogInformation("Vessel {Name} (MMSI {Mmsi}) entered monitored area",
                vessel.DisplayName, vessel.Mmsi);
            VesselAreaChanged?.Invoke(this, evt);
            WeakReferenceMessenger.Default.Send(new VesselAreaNotification(evt));
        }
        else if (!isInside && wasInside)
        {
            var evt = new VesselAreaEvent
            {
                Vessel = vessel,
                Entered = false,
                Timestamp = DateTime.UtcNow,
                ZoneName = zoneName
            };
            _logger.LogInformation("Vessel {Name} (MMSI {Mmsi}) left monitored area",
                vessel.DisplayName, vessel.Mmsi);
            VesselAreaChanged?.Invoke(this, evt);
            WeakReferenceMessenger.Default.Send(new VesselAreaNotification(evt));
        }
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselUpdated;
        _vesselStore.VesselUpdated -= OnVesselUpdated;
        _vesselStore.StoreCleared -= _onStoreCleared;
    }
}
