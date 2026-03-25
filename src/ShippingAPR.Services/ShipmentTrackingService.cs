using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Tracks user-defined shipments (origin→destination) by matching them to
/// AIS-tracked vessels, calculating progress, and detecting delays/arrivals.
/// </summary>
public sealed class ShipmentTrackingService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly IPortRepository _portRepository;
    private readonly NotificationService _notificationService;
    private readonly ILogger<ShipmentTrackingService> _logger;
    private readonly List<Shipment> _shipments = [];
    private readonly object _lock = new();

    private const double ArrivalRadiusNm = 5.0;

    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR", "shipments.json");

    public event EventHandler<Shipment>? ShipmentUpdated;

    public ShipmentTrackingService(
        IVesselStore vesselStore,
        IPortRepository portRepository,
        NotificationService notificationService,
        ILogger<ShipmentTrackingService> logger)
    {
        _vesselStore = vesselStore;
        _portRepository = portRepository;
        _notificationService = notificationService;
        _logger = logger;

        Load();
        _vesselStore.VesselUpdated += OnVesselUpdate;
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is null) return;

        lock (_lock)
        {
            foreach (var shipment in _shipments.Where(s =>
                s.Status is ShipmentStatus.InTransit or ShipmentStatus.Pending))
            {
                // Auto-assign: if a vessel's destination matches and no vessel assigned
                if (shipment.AssignedMmsi is null && !string.IsNullOrEmpty(vessel.StaticData?.Destination))
                {
                    var destPort = _portRepository.FindByName(shipment.DestinationPortName);
                    var vesselDest = _portRepository.FindByName(vessel.StaticData.Destination);
                    if (destPort is not null && vesselDest is not null &&
                        destPort.Name == vesselDest.Name)
                    {
                        shipment.AssignedMmsi = vessel.Mmsi;
                        shipment.Status = ShipmentStatus.InTransit;
                        ShipmentUpdated?.Invoke(this, shipment);
                    }
                }

                // Check assigned vessel for arrival
                if (shipment.AssignedMmsi == vessel.Mmsi &&
                    shipment.Status == ShipmentStatus.InTransit)
                {
                    var destPort = _portRepository.FindByName(shipment.DestinationPortName);
                    if (destPort is not null)
                    {
                        var distNm = HaversineCalculator.DistanceInNauticalMiles(
                            vessel.CurrentPosition.Latitude,
                            vessel.CurrentPosition.Longitude,
                            destPort.Latitude,
                            destPort.Longitude);

                        if (distNm <= ArrivalRadiusNm)
                        {
                            shipment.Status = ShipmentStatus.Arrived;
                            shipment.CompletedAt = DateTime.UtcNow;
                            _notificationService.Publish(new NotificationMessage
                            {
                                Title = "Shipment Arrived",
                                Body = $"\"{shipment.Name}\" has arrived at {shipment.DestinationPortName}",
                                Type = NotificationType.Info
                            });
                            ShipmentUpdated?.Invoke(this, shipment);
                            Save();
                        }
                    }
                }
            }
        }
    }

    public Shipment AddShipment(string name, string originPort, string destPort)
    {
        var shipment = new Shipment
        {
            Name = name,
            OriginPortName = originPort,
            DestinationPortName = destPort
        };

        lock (_lock) { _shipments.Add(shipment); }
        Save();
        _logger.LogInformation("Added shipment: {Name} ({Origin} → {Dest})", name, originPort, destPort);
        return shipment;
    }

    public void RemoveShipment(string id)
    {
        lock (_lock) { _shipments.RemoveAll(s => s.Id == id); }
        Save();
    }

    public void AssignVessel(string shipmentId, int mmsi)
    {
        lock (_lock)
        {
            var shipment = _shipments.FirstOrDefault(s => s.Id == shipmentId);
            if (shipment is not null)
            {
                shipment.AssignedMmsi = mmsi;
                shipment.Status = ShipmentStatus.InTransit;
                Save();
                ShipmentUpdated?.Invoke(this, shipment);
            }
        }
    }

    public IReadOnlyList<Shipment> GetAll()
    {
        lock (_lock) { return _shipments.ToList().AsReadOnly(); }
    }

    public ShipmentProgress? GetProgress(string shipmentId)
    {
        Shipment? shipment;
        lock (_lock) { shipment = _shipments.FirstOrDefault(s => s.Id == shipmentId); }
        if (shipment is null) return null;

        var originPort = _portRepository.FindByName(shipment.OriginPortName);
        var destPort = _portRepository.FindByName(shipment.DestinationPortName);
        if (originPort is null || destPort is null)
            return new ShipmentProgress
            {
                ShipmentId = shipmentId,
                PercentComplete = 0,
                AssignedVesselName = null
            };

        var totalDistance = HaversineCalculator.DistanceInNauticalMiles(
            originPort.Latitude, originPort.Longitude,
            destPort.Latitude, destPort.Longitude);

        if (shipment.AssignedMmsi is null)
            return new ShipmentProgress
            {
                ShipmentId = shipmentId,
                TotalDistanceNm = Math.Round(totalDistance, 1)
            };

        var vessel = _vesselStore.GetByMmsi(shipment.AssignedMmsi.Value);
        if (vessel?.CurrentPosition is null)
            return new ShipmentProgress
            {
                ShipmentId = shipmentId,
                TotalDistanceNm = Math.Round(totalDistance, 1),
                AssignedVesselName = vessel?.DisplayName
            };

        var distanceRemaining = HaversineCalculator.DistanceInNauticalMiles(
            vessel.CurrentPosition.Latitude, vessel.CurrentPosition.Longitude,
            destPort.Latitude, destPort.Longitude);

        var distanceTraveled = totalDistance - distanceRemaining;
        var percentComplete = totalDistance > 0
            ? Math.Clamp(distanceTraveled / totalDistance * 100, 0, 100)
            : 0;

        var speed = vessel.CurrentPosition.SpeedOverGround;
        TimeSpan? eta = speed > 0.5
            ? TimeSpan.FromHours(distanceRemaining / speed)
            : null;

        return new ShipmentProgress
        {
            ShipmentId = shipmentId,
            PercentComplete = Math.Round(percentComplete, 1),
            DistanceTraveledNm = Math.Round(Math.Max(0, distanceTraveled), 1),
            DistanceRemainingNm = Math.Round(distanceRemaining, 1),
            TotalDistanceNm = Math.Round(totalDistance, 1),
            EstimatedTimeRemaining = eta,
            EstimatedArrival = eta.HasValue ? DateTime.UtcNow + eta.Value : null,
            AssignedVesselName = vessel.DisplayName,
            CurrentSpeedKnots = speed
        };
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(DataPath)!;
            Directory.CreateDirectory(dir);
            List<Shipment> copy;
            lock (_lock) { copy = _shipments.ToList(); }
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save shipments");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(DataPath)) return;
            var json = File.ReadAllText(DataPath);
            var shipments = JsonSerializer.Deserialize<List<Shipment>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (shipments is not null)
            {
                lock (_lock) { _shipments.AddRange(shipments); }
                _logger.LogInformation("Loaded {Count} shipments", shipments.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load shipments");
        }
    }

    public void Dispose()
    {
        _vesselStore.VesselUpdated -= OnVesselUpdate;
        Save();
    }
}
