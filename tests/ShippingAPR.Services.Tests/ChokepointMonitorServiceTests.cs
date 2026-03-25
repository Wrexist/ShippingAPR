using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.Services.Tests;

public class ChokepointMonitorServiceTests
{
    private readonly Mock<IVesselStore> _storeMock = new();
    private readonly Mock<ILogger<ChokepointMonitorService>> _loggerMock = new();
    private readonly ChokepointMonitorService _service;

    public ChokepointMonitorServiceTests()
    {
        _storeMock.Setup(s => s.Vessels).Returns(new Dictionary<int, Vessel>());
        _service = new ChokepointMonitorService(_storeMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void Chokepoints_ContainsSevenMajorStraits()
    {
        ChokepointMonitorService.Chokepoints.Should().HaveCount(7);
    }

    [Fact]
    public void Chokepoints_IncludesSuezCanal()
    {
        ChokepointMonitorService.Chokepoints
            .Should().Contain(c => c.Name == "Suez Canal");
    }

    [Fact]
    public void GetStatus_UnknownChokepoint_ReturnsEmptyStatus()
    {
        var status = _service.GetStatus("Nonexistent Strait");

        status.Name.Should().Be("Nonexistent Strait");
        status.VesselsInTransit.Should().Be(0);
    }

    [Fact]
    public void GetStatus_NoVessels_ReturnsLowCongestion()
    {
        var status = _service.GetStatus("Suez Canal");

        status.CongestionLevel.Should().Be(ChokepointCongestion.Low);
        status.VesselsInTransit.Should().Be(0);
        status.TransitsLast24h.Should().Be(0);
    }

    [Fact]
    public void GetAllStatuses_ReturnsAllChokepoints()
    {
        var statuses = _service.GetAllStatuses();

        statuses.Should().HaveCount(7);
        statuses.Select(s => s.Name).Should().Contain("Panama Canal");
        statuses.Select(s => s.Name).Should().Contain("Strait of Malacca");
    }

    [Fact]
    public void VesselEnteringChokepoint_TriggersTransitDetected()
    {
        ChokepointTransitRecord? firedRecord = null;
        _service.TransitDetected += (_, record) => firedRecord = record;

        // Create vessel inside Suez Canal bounds (29.8-31.3, 32.2-32.6)
        var vessel = CreateVessel(mmsi: 100000001, lat: 30.5, lon: 32.4);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);

        firedRecord.Should().NotBeNull();
        firedRecord!.VesselName.Should().Be("Test Vessel");
    }

    [Fact]
    public void VesselEnteringChokepoint_IncreasesTransitCount()
    {
        var vessel = CreateVessel(mmsi: 100000001, lat: 30.5, lon: 32.4);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);

        var status = _service.GetStatus("Suez Canal");
        status.VesselsInTransit.Should().Be(1);
        status.TransitsLast24h.Should().Be(1);
    }

    [Fact]
    public void VesselLeavingChokepoint_DecreasesCount()
    {
        var vessel = CreateVessel(mmsi: 100000001, lat: 30.5, lon: 32.4);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);

        // Move vessel outside bounds
        vessel.UpdatePosition(new VesselPosition
        {
            Latitude = 28.0, Longitude = 32.4,
            SpeedOverGround = 10.0, Timestamp = DateTime.UtcNow
        });
        _storeMock.Raise(s => s.VesselUpdated += null!, null!, vessel);

        var status = _service.GetStatus("Suez Canal");
        status.VesselsInTransit.Should().Be(0);
    }

    [Fact]
    public void VesselOutsideAllChokepoints_NoTransitRecorded()
    {
        ChokepointTransitRecord? firedRecord = null;
        _service.TransitDetected += (_, record) => firedRecord = record;

        // Open ocean position
        var vessel = CreateVessel(mmsi: 100000001, lat: 0.0, lon: 0.0);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);

        firedRecord.Should().BeNull();
    }

    [Fact]
    public void StoreCleared_ResetsAllZones()
    {
        var vessel = CreateVessel(mmsi: 100000001, lat: 30.5, lon: 32.4);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);

        _storeMock.Raise(s => s.StoreCleared += null!, null!, EventArgs.Empty);

        var status = _service.GetStatus("Suez Canal");
        status.VesselsInTransit.Should().Be(0);
    }

    private static Vessel CreateVessel(int mmsi, double lat, double lon)
    {
        var vessel = new Vessel(mmsi);
        vessel.UpdatePosition(
            new VesselPosition
            {
                Latitude = lat, Longitude = lon,
                SpeedOverGround = 10.0, CourseOverGround = 180.0,
                TrueHeading = 180.0, Status = NavigationalStatus.UnderWayUsingEngine,
                Timestamp = DateTime.UtcNow
            });
        vessel.UpdateStaticData(
            new VesselStaticData { Name = "Test Vessel", ShipType = VesselType.Cargo });
        return vessel;
    }
}
