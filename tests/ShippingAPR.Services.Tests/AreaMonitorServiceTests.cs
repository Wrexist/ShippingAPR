using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class AreaMonitorServiceTests
{
    private readonly VesselStore _vesselStore = new();
    private readonly AreaMonitorService _monitor;

    public AreaMonitorServiceTests()
    {
        var logger = Mock.Of<ILogger<AreaMonitorService>>();
        _monitor = new AreaMonitorService(_vesselStore, logger);
        _monitor.SetMonitoredArea(new BoundingBox(57.65, 11.80, 57.75, 12.05));
    }

    [Fact]
    public void VesselEnteringArea_FiresEnteredEvent()
    {
        VesselAreaEvent? receivedEvent = null;
        _monitor.VesselAreaChanged += (_, evt) => receivedEvent = evt;

        // First update: outside area
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.5, 11.9), null);
        receivedEvent.Should().BeNull(); // First position establishes baseline

        // Second update: inside area
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.70, 11.95), null);

        receivedEvent.Should().NotBeNull();
        receivedEvent!.Entered.Should().BeTrue();
    }

    [Fact]
    public void VesselLeavingArea_FiresLeftEvent()
    {
        VesselAreaEvent? receivedEvent = null;

        // First: inside area (establishes baseline as "inside")
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.70, 11.95), null);

        _monitor.VesselAreaChanged += (_, evt) => receivedEvent = evt;

        // Second: outside area
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.5, 11.9), null);

        receivedEvent.Should().NotBeNull();
        receivedEvent!.Entered.Should().BeFalse();
    }

    [Fact]
    public void VesselStayingInside_DoesNotFireEvent()
    {
        var eventCount = 0;

        // Start inside
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.70, 11.95), null);

        _monitor.VesselAreaChanged += (_, _) => eventCount++;

        // Stay inside
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.71, 11.96), null);
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.72, 11.97), null);

        eventCount.Should().Be(0);
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
