using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class MaritimeIncidentServiceTests : IDisposable
{
    private readonly Mock<IVesselStore> _storeMock = new();
    private readonly Mock<ILogger<MaritimeIncidentService>> _loggerMock = new();
    private readonly MaritimeIncidentService _service;

    public MaritimeIncidentServiceTests()
    {
        _storeMock.Setup(s => s.Vessels).Returns(new Dictionary<int, Vessel>());

        var areaMonitor = new AreaMonitorService(_storeMock.Object, Mock.Of<ILogger<AreaMonitorService>>());
        var notifService = new NotificationService(areaMonitor,
            Mock.Of<ILogger<NotificationService>>(),
            Options.Create(new TrackingOptions()));

        _service = new MaritimeIncidentService(
            _storeMock.Object, notifService, _loggerMock.Object);
    }

    [Fact]
    public void GetActive_ReturnsSeededIncidents()
    {
        var active = _service.GetActive();
        active.Should().NotBeEmpty();
        active.Should().Contain(i => i.Title.Contains("Piracy"));
    }

    [Fact]
    public void AddIncident_IncreasesCount()
    {
        var initialCount = _service.GetAll().Count;

        _service.AddIncident(new MaritimeIncident
        {
            Title = "Test Incident",
            Latitude = 0, Longitude = 0,
            Severity = IncidentSeverity.Info,
            Category = IncidentCategory.GeneralNews,
            Source = "Test"
        });

        _service.GetAll().Count.Should().Be(initialCount + 1);
    }

    [Fact]
    public void AddIncident_FiresEvent()
    {
        MaritimeIncident? fired = null;
        _service.IncidentAdded += (_, i) => fired = i;

        _service.AddIncident(new MaritimeIncident
        {
            Title = "Event Test",
            Latitude = 10, Longitude = 20,
            Severity = IncidentSeverity.Warning,
            Category = IncidentCategory.Piracy,
            Source = "Test"
        });

        fired.Should().NotBeNull();
        fired!.Title.Should().Be("Event Test");
    }

    [Fact]
    public void GetByCategory_FiltersCorrectly()
    {
        var piracy = _service.GetByCategory(IncidentCategory.Piracy);
        piracy.Should().NotBeEmpty();
        piracy.Should().OnlyContain(i => i.Category == IncidentCategory.Piracy);
    }

    [Fact]
    public void GetNearby_FiltersbyDistance()
    {
        _service.AddIncident(new MaritimeIncident
        {
            Title = "Nearby Incident",
            Latitude = 57.0, Longitude = 12.0,
            Severity = IncidentSeverity.Warning,
            Category = IncidentCategory.NavigationalWarning,
            Source = "Test"
        });

        var nearby = _service.GetNearby(57.1, 12.1, 50);
        nearby.Should().Contain(i => i.Title == "Nearby Incident");
    }

    [Fact]
    public void GetNearby_ExcludesDistantIncidents()
    {
        _service.AddIncident(new MaritimeIncident
        {
            Title = "Far Away",
            Latitude = -30.0, Longitude = 150.0,
            Severity = IncidentSeverity.Warning,
            Category = IncidentCategory.Weather,
            Source = "Test"
        });

        var nearby = _service.GetNearby(57.0, 12.0, 50);
        nearby.Should().NotContain(i => i.Title == "Far Away");
    }

    [Fact]
    public void ExpiredIncident_ExcludedFromActive()
    {
        _service.AddIncident(new MaritimeIncident
        {
            Title = "Expired",
            Latitude = 0, Longitude = 0,
            Severity = IncidentSeverity.Info,
            Category = IncidentCategory.GeneralNews,
            Source = "Test",
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Already expired
        });

        _service.GetActive().Should().NotContain(i => i.Title == "Expired");
    }

    [Fact]
    public void RemoveExpired_CleansUpExpiredIncidents()
    {
        var initialCount = _service.GetAll().Count;
        _service.AddIncident(new MaritimeIncident
        {
            Title = "Will Expire",
            Latitude = 0, Longitude = 0,
            Severity = IncidentSeverity.Info,
            Category = IncidentCategory.GeneralNews,
            Source = "Test",
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        });

        _service.RemoveExpired();

        _service.GetAll().Should().NotContain(i => i.Title == "Will Expire");
    }

    [Fact]
    public void ProximityAlert_FiredWhenVesselNearWarning()
    {
        (MaritimeIncident Incident, Vessel Vessel)? alertFired = null;
        _service.ProximityAlertTriggered += (_, args) => alertFired = args;

        // Add a high-severity incident near a specific location
        _service.AddIncident(new MaritimeIncident
        {
            Title = "Danger Zone",
            Latitude = 57.0, Longitude = 12.0,
            Severity = IncidentSeverity.Warning,
            Category = IncidentCategory.Piracy,
            Source = "Test"
        });

        // Simulate vessel update near the incident
        var vessel = new Vessel(123456789);
        vessel.UpdatePosition(
            new VesselPosition
            {
                Latitude = 57.05, Longitude = 12.05,
                SpeedOverGround = 14.0, Timestamp = DateTime.UtcNow
            });
        vessel.UpdateStaticData(
            new VesselStaticData { Name = "Near Vessel" });

        _storeMock.Raise(s => s.VesselUpdated += null!, null!, vessel);

        alertFired.Should().NotBeNull();
        alertFired!.Value.Incident.Title.Should().Be("Danger Zone");
    }

    public void Dispose() => _service.Dispose();
}
