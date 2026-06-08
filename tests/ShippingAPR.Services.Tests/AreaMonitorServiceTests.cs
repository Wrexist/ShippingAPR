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

    [Fact]
    public void VesselJitteringOnBoundary_DoesNotFlap()
    {
        // Monitored area's north edge is at lat 57.75; the hysteresis deadband extends a
        // little beyond it. A vessel oscillating across that edge must not flap alerts.
        var events = new List<VesselAreaEvent>();
        _monitor.VesselAreaChanged += (_, evt) => events.Add(evt);

        // Start clearly outside, then cross clearly inside → a single "entered".
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.90, 11.95), null); // baseline outside
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.70, 11.95), null); // enters

        // Now jitter just across the boundary, within the deadband — no further alerts.
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.752, 11.95), null); // just outside box
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.748, 11.95), null); // just inside box
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.752, 11.95), null);
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.748, 11.95), null);

        events.Should().ContainSingle();
        events[0].Entered.Should().BeTrue();
    }

    [Fact]
    public void VesselLeavingBeyondMargin_FiresLeftOnce()
    {
        var events = new List<VesselAreaEvent>();

        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.70, 11.95), null); // baseline inside

        _monitor.VesselAreaChanged += (_, evt) => events.Add(evt);

        // Drift just outside the box but within the deadband — still considered inside.
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.752, 11.95), null);
        events.Should().BeEmpty();

        // Move clearly beyond the deadband — a confirmed exit, fired exactly once.
        _vesselStore.AddOrUpdate(100000001, CreatePosition(57.90, 11.95), null);
        events.Should().ContainSingle();
        events[0].Entered.Should().BeFalse();
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
