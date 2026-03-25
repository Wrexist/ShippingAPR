using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Manages maritime incidents, navigational warnings, and news items.
/// Provides proximity alerts when tracked vessels are near active incidents.
/// </summary>
public sealed class MaritimeIncidentService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly NotificationService _notificationService;
    private readonly ILogger<MaritimeIncidentService> _logger;

    private readonly List<MaritimeIncident> _incidents = [];
    private readonly ConcurrentDictionary<string, HashSet<int>> _alertedVessels = new();
    private readonly object _lock = new();

    private const double ProximityAlertRadiusNm = 50.0;
    private const int MaxIncidents = 500;

    public event EventHandler<MaritimeIncident>? IncidentAdded;
    public event EventHandler<(MaritimeIncident Incident, Vessel Vessel)>? ProximityAlertTriggered;

    public MaritimeIncidentService(
        IVesselStore vesselStore,
        NotificationService notificationService,
        ILogger<MaritimeIncidentService> logger)
    {
        _vesselStore = vesselStore;
        _notificationService = notificationService;
        _logger = logger;

        // Seed with initial global maritime awareness data
        SeedInitialIncidents();

        _vesselStore.VesselUpdated += OnVesselUpdate;
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        if (vessel.CurrentPosition is null) return;

        lock (_lock)
        {
            foreach (var incident in _incidents.Where(i => i.IsActive &&
                i.Severity >= IncidentSeverity.Warning))
            {
                var distance = HaversineCalculator.DistanceInNauticalMiles(
                    vessel.CurrentPosition.Latitude,
                    vessel.CurrentPosition.Longitude,
                    incident.Latitude,
                    incident.Longitude);

                if (distance > ProximityAlertRadiusNm) continue;

                var alertedForIncident = _alertedVessels.GetOrAdd(incident.Id, _ => new HashSet<int>());
                if (!alertedForIncident.Add(vessel.Mmsi)) continue;

                _notificationService.Publish(new NotificationMessage
                {
                    Title = $"Proximity Alert: {incident.Category}",
                    Body = $"{vessel.DisplayName} is {distance:F0} NM from {incident.Title}",
                    Type = NotificationType.Warning
                });

                ProximityAlertTriggered?.Invoke(this, (incident, vessel));
            }
        }
    }

    public void AddIncident(MaritimeIncident incident)
    {
        lock (_lock)
        {
            _incidents.Add(incident);
            if (_incidents.Count > MaxIncidents)
                _incidents.RemoveAt(0);
        }

        IncidentAdded?.Invoke(this, incident);
        _logger.LogInformation("Maritime incident added: {Title} ({Category}/{Severity})",
            incident.Title, incident.Category, incident.Severity);
    }

    public IReadOnlyList<MaritimeIncident> GetAll()
    {
        lock (_lock) { return _incidents.ToList().AsReadOnly(); }
    }

    public IReadOnlyList<MaritimeIncident> GetActive()
    {
        lock (_lock) { return _incidents.Where(i => i.IsActive).Reverse().ToList().AsReadOnly(); }
    }

    public IReadOnlyList<MaritimeIncident> GetByCategory(IncidentCategory category)
    {
        lock (_lock)
        {
            return _incidents
                .Where(i => i.Category == category && i.IsActive)
                .Reverse()
                .ToList()
                .AsReadOnly();
        }
    }

    public IReadOnlyList<MaritimeIncident> GetNearby(double lat, double lon, double radiusNm = 100)
    {
        lock (_lock)
        {
            return _incidents
                .Where(i => i.IsActive &&
                    HaversineCalculator.DistanceInNauticalMiles(lat, lon, i.Latitude, i.Longitude) <= radiusNm)
                .Reverse()
                .ToList()
                .AsReadOnly();
        }
    }

    public void RemoveExpired()
    {
        lock (_lock)
        {
            var removed = _incidents.RemoveAll(i => !i.IsActive);
            if (removed > 0)
                _logger.LogDebug("Removed {Count} expired maritime incidents", removed);
        }
    }

    private void SeedInitialIncidents()
    {
        // Seed with representative global awareness zones
        AddIncident(new MaritimeIncident
        {
            Title = "Gulf of Guinea Piracy Zone",
            Description = "High risk area for piracy and armed robbery. Exercise caution and report suspicious activity.",
            Latitude = 3.0, Longitude = 3.0,
            Severity = IncidentSeverity.Warning,
            Category = IncidentCategory.Piracy,
            Source = "MDAT-GoG",
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        AddIncident(new MaritimeIncident
        {
            Title = "Strait of Hormuz - Heightened Security",
            Description = "Increased naval activity. Vessels should maintain AIS and monitor VHF Ch 16.",
            Latitude = 26.5, Longitude = 56.3,
            Severity = IncidentSeverity.Advisory,
            Category = IncidentCategory.SecurityAlert,
            Source = "UKMTO",
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        AddIncident(new MaritimeIncident
        {
            Title = "Red Sea / Bab el-Mandeb - Navigation Advisory",
            Description = "Exercise extreme caution. Multiple vessel attacks reported. Consider alternative routing.",
            Latitude = 13.0, Longitude = 43.0,
            Severity = IncidentSeverity.Critical,
            Category = IncidentCategory.SecurityAlert,
            Source = "UKMTO",
            ExpiresAt = DateTime.UtcNow.AddDays(60)
        });
        AddIncident(new MaritimeIncident
        {
            Title = "North Sea - Offshore Wind Farm Construction",
            Description = "Navigational restriction zone active. Multiple construction vessels operating.",
            Latitude = 54.5, Longitude = 3.5,
            Severity = IncidentSeverity.Advisory,
            Category = IncidentCategory.NavigationalWarning,
            Source = "NAVAREA I",
            ExpiresAt = DateTime.UtcNow.AddDays(180)
        });
        AddIncident(new MaritimeIncident
        {
            Title = "Baltic Sea - Ice Advisory",
            Description = "Ice conditions developing in northern Baltic. Icebreaker assistance may be required.",
            Latitude = 62.0, Longitude = 20.0,
            Severity = IncidentSeverity.Info,
            Category = IncidentCategory.Weather,
            Source = "SMHI",
            ExpiresAt = DateTime.UtcNow.AddDays(45)
        });
    }

    public void Dispose()
    {
        _vesselStore.VesselUpdated -= OnVesselUpdate;
    }
}
