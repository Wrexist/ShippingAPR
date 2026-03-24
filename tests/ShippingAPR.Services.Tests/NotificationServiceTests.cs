using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class NotificationServiceTests
{
    private readonly VesselStore _vesselStore = new();
    private readonly AreaMonitorService _areaMonitor;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        var areaLogger = Mock.Of<ILogger<AreaMonitorService>>();
        _areaMonitor = new AreaMonitorService(_vesselStore, areaLogger);

        var notifLogger = Mock.Of<ILogger<NotificationService>>();
        _service = new NotificationService(_areaMonitor, notifLogger);
    }

    [Fact]
    public void Publish_AddsToHistory()
    {
        var msg = new NotificationMessage
        {
            Title = "Test",
            Body = "Test body",
            Type = NotificationType.Info
        };

        _service.Publish(msg);

        _service.History.Should().HaveCount(1);
        _service.History[0].Title.Should().Be("Test");
    }

    [Fact]
    public void History_CappedAtMaxSize()
    {
        for (int i = 0; i < 120; i++)
        {
            _service.Publish(new NotificationMessage
            {
                Title = $"Notification {i}",
                Body = $"Body {i}",
                Type = NotificationType.Info
            });
        }

        _service.History.Should().HaveCount(100);
        // Most recent should be at the end
        _service.History.Last().Title.Should().Be("Notification 119");
    }

    [Fact]
    public void VesselEnteringArea_PublishesNotification()
    {
        _areaMonitor.SetMonitoredArea(new BoundingBox(57.65, 11.80, 57.75, 12.05));

        // First update: outside area (establishes baseline)
        _vesselStore.AddOrUpdate(1, CreatePosition(57.5, 11.9),
            new VesselStaticData { Name = "TEST SHIP" });

        // Second update: inside area (triggers entry)
        _vesselStore.AddOrUpdate(1, CreatePosition(57.70, 11.95), null);

        _service.History.Should().HaveCount(1);
        _service.History[0].Type.Should().Be(NotificationType.VesselEntered);
        _service.History[0].Title.Should().Contain("Entered");
    }

    [Fact]
    public void VesselLeavingArea_PublishesNotification()
    {
        _areaMonitor.SetMonitoredArea(new BoundingBox(57.65, 11.80, 57.75, 12.05));

        // Start inside
        _vesselStore.AddOrUpdate(1, CreatePosition(57.70, 11.95),
            new VesselStaticData { Name = "TEST SHIP" });

        // Move outside
        _vesselStore.AddOrUpdate(1, CreatePosition(57.5, 11.9), null);

        _service.History.Should().HaveCount(1);
        _service.History[0].Type.Should().Be(NotificationType.VesselLeft);
        _service.History[0].Title.Should().Contain("Left");
    }

    private static VesselPosition CreatePosition(double lat, double lon) =>
        new()
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = 10.0,
            CourseOverGround = 0,
            TrueHeading = 0,
            Status = NavigationalStatus.UnderWayUsingEngine,
            Timestamp = DateTime.UtcNow
        };
}
