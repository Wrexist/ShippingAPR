using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class PortActivityServiceTests : IDisposable
{
    private readonly VesselStore _store = new();
    private readonly Mock<IPortRepository> _portRepo = new();
    private readonly NotificationService _notificationService;
    private readonly PortActivityService _sut;

    public PortActivityServiceTests()
    {
        var areaMonitor = new AreaMonitorService(_store, NullLogger<AreaMonitorService>.Instance);
        _notificationService = new NotificationService(
            areaMonitor,
            NullLogger<NotificationService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new TrackingOptions()));

        // Set up a test port at Gothenburg
        _portRepo.Setup(x => x.GetAll()).Returns(new List<Port>
        {
            new("SEGOT", "Gothenburg", "SE", 57.7089, 11.9746)
        });

        _sut = new PortActivityService(
            _store,
            _portRepo.Object,
            _notificationService,
            NullLogger<PortActivityService>.Instance);
    }

    [Fact]
    public void VesselNearPort_RegistersArrival()
    {
        PortActivityRecord? recorded = null;
        _sut.ActivityRecorded += (_, r) => recorded = r;

        // Add vessel very close to Gothenburg port
        _store.AddOrUpdate(123,
            new VesselPosition
            {
                Latitude = 57.709,
                Longitude = 11.975,
                SpeedOverGround = 3,
                CourseOverGround = 0,
                TrueHeading = 0,
                Status = NavigationalStatus.UnderWayUsingEngine
            }, null);

        recorded.Should().NotBeNull();
        recorded!.ActivityType.Should().Be(PortActivityType.Arrival);
        recorded.PortName.Should().Be("Gothenburg");
    }

    [Fact]
    public void VesselFarFromPort_NoActivity()
    {
        PortActivityRecord? recorded = null;
        _sut.ActivityRecorded += (_, r) => recorded = r;

        // Add vessel far from Gothenburg
        _store.AddOrUpdate(123,
            new VesselPosition
            {
                Latitude = 55.0,
                Longitude = 13.0,
                SpeedOverGround = 12,
                CourseOverGround = 180,
                TrueHeading = 180,
                Status = NavigationalStatus.UnderWayUsingEngine
            }, null);

        recorded.Should().BeNull();
    }

    [Fact]
    public void GetCongestion_ReturnsSnapshot()
    {
        // Add a vessel in port
        _store.AddOrUpdate(123,
            new VesselPosition
            {
                Latitude = 57.709,
                Longitude = 11.975,
                SpeedOverGround = 1,
                CourseOverGround = 0,
                TrueHeading = 0,
                Status = NavigationalStatus.Moored
            }, null);

        var snapshot = _sut.GetCongestion("Gothenburg");
        snapshot.Should().NotBeNull();
        snapshot.PortName.Should().Be("Gothenburg");
        snapshot.VesselsInPort.Should().BeGreaterThanOrEqualTo(1);
        snapshot.ArrivalsLast24h.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void GetActivePortNames_ReturnsPortsWithVessels()
    {
        _store.AddOrUpdate(123,
            new VesselPosition
            {
                Latitude = 57.709,
                Longitude = 11.975,
                SpeedOverGround = 1,
                CourseOverGround = 0,
                TrueHeading = 0,
                Status = NavigationalStatus.Moored
            }, null);

        var active = _sut.GetActivePortNames();
        active.Should().Contain("Gothenburg");
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
