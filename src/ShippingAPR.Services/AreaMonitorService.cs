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
}

public sealed class VesselAreaNotification : ValueChangedMessage<VesselAreaEvent>
{
    public VesselAreaNotification(VesselAreaEvent value) : base(value) { }
}

public sealed class AreaMonitorService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly ILogger<AreaMonitorService> _logger;
    private readonly ConcurrentDictionary<int, bool> _previousState = new();
    private readonly EventHandler _onStoreCleared;
    private BoundingBox? _monitoredArea;

    public event EventHandler<VesselAreaEvent>? VesselAreaChanged;

    public AreaMonitorService(
        IVesselStore vesselStore,
        ILogger<AreaMonitorService> logger)
    {
        _vesselStore = vesselStore;
        _logger = logger;

        _onStoreCleared = (_, _) => _previousState.Clear();
        _vesselStore.VesselAdded += OnVesselUpdated;
        _vesselStore.VesselUpdated += OnVesselUpdated;
        _vesselStore.StoreCleared += _onStoreCleared;
    }

    public void SetMonitoredArea(BoundingBox area)
    {
        _monitoredArea = area;
        _previousState.Clear();
    }

    private void OnVesselUpdated(object? sender, Vessel vessel)
    {
        if (_monitoredArea is null || vessel.CurrentPosition is null)
            return;

        var isInside = _monitoredArea.Contains(
            vessel.CurrentPosition.Latitude,
            vessel.CurrentPosition.Longitude);

        // Atomically read the previous state and update to the new state.
        // Default to false so a vessel appearing inside the area for the
        // first time is correctly detected as an "entry" event.
        var wasInside = false;
        _previousState.AddOrUpdate(
            vessel.Mmsi,
            isInside,
            (_, old) => { wasInside = old; return isInside; });

        // Detect transitions
        if (isInside && !wasInside)
        {
            var evt = new VesselAreaEvent
            {
                Vessel = vessel,
                Entered = true,
                Timestamp = DateTime.UtcNow
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
                Timestamp = DateTime.UtcNow
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
