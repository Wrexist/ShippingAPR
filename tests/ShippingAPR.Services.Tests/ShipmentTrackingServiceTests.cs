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

public class ShipmentTrackingServiceTests : IDisposable
{
    private readonly Mock<IVesselStore> _storeMock = new();
    private readonly Mock<IPortRepository> _portMock = new();
    private readonly Mock<ILogger<ShipmentTrackingService>> _loggerMock = new();
    private readonly ShipmentTrackingService _service;

    public ShipmentTrackingServiceTests()
    {
        _storeMock.Setup(s => s.Vessels).Returns(new Dictionary<int, Vessel>());

        var areaMonitor = new AreaMonitorService(_storeMock.Object, Mock.Of<ILogger<AreaMonitorService>>());
        var notifService = new NotificationService(areaMonitor,
            Mock.Of<ILogger<NotificationService>>(),
            Options.Create(new TrackingOptions()));

        _service = new ShipmentTrackingService(
            _storeMock.Object, _portMock.Object,
            notifService, _loggerMock.Object);
    }

    [Fact]
    public void AddShipment_CreatesShipmentWithPendingStatus()
    {
        var shipment = _service.AddShipment("My Cargo", "Shanghai", "Rotterdam");

        shipment.Name.Should().Be("My Cargo");
        shipment.OriginPortName.Should().Be("Shanghai");
        shipment.DestinationPortName.Should().Be("Rotterdam");
        shipment.Status.Should().Be(ShipmentStatus.Pending);
    }

    [Fact]
    public void GetAll_ReturnsAddedShipments()
    {
        _service.AddShipment("Shipment 1", "A", "B");
        _service.AddShipment("Shipment 2", "C", "D");

        _service.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void RemoveShipment_RemovesById()
    {
        var shipment = _service.AddShipment("To Remove", "A", "B");

        _service.RemoveShipment(shipment.Id);

        _service.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void AssignVessel_UpdatesShipmentStatus()
    {
        var shipment = _service.AddShipment("Test", "A", "B");

        _service.AssignVessel(shipment.Id, 123456789);

        var updated = _service.GetAll().First();
        updated.AssignedMmsi.Should().Be(123456789);
        updated.Status.Should().Be(ShipmentStatus.InTransit);
    }

    [Fact]
    public void GetProgress_NoAssignedVessel_ReturnsBasicInfo()
    {
        _portMock.Setup(p => p.FindByName("Shanghai"))
            .Returns(new Port("CNSHA", "Shanghai", "CN", 31.2, 121.5));
        _portMock.Setup(p => p.FindByName("Rotterdam"))
            .Returns(new Port("NLRTM", "Rotterdam", "NL", 51.9, 4.5));

        var shipment = _service.AddShipment("Test", "Shanghai", "Rotterdam");
        var progress = _service.GetProgress(shipment.Id);

        progress.Should().NotBeNull();
        progress!.TotalDistanceNm.Should().BeGreaterThan(0);
        progress.AssignedVesselName.Should().BeNull();
    }

    [Fact]
    public void GetProgress_WithAssignedVessel_CalculatesProgress()
    {
        var originPort = new Port("CNSHA", "Shanghai", "CN", 31.2, 121.5);
        var destPort = new Port("NLRTM", "Rotterdam", "NL", 51.9, 4.5);
        _portMock.Setup(p => p.FindByName("Shanghai")).Returns(originPort);
        _portMock.Setup(p => p.FindByName("Rotterdam")).Returns(destPort);

        var vessel = new Vessel(123456789);
        vessel.UpdatePosition(
            new VesselPosition
            {
                Latitude = 40.0, Longitude = 60.0,
                SpeedOverGround = 14.0, Timestamp = DateTime.UtcNow
            });
        vessel.UpdateStaticData(
            new VesselStaticData { Name = "Carrier" });
        _storeMock.Setup(s => s.GetByMmsi(123456789)).Returns(vessel);

        var shipment = _service.AddShipment("Test", "Shanghai", "Rotterdam");
        _service.AssignVessel(shipment.Id, 123456789);
        var progress = _service.GetProgress(shipment.Id);

        progress.Should().NotBeNull();
        progress!.PercentComplete.Should().BeGreaterThan(0);
        progress.DistanceRemainingNm.Should().BeGreaterThan(0);
        progress.AssignedVesselName.Should().Be("Carrier");
    }

    [Fact]
    public void GetProgress_NonexistentShipment_ReturnsNull()
    {
        _service.GetProgress("nonexistent").Should().BeNull();
    }

    [Fact]
    public void GetProgress_UnknownPorts_ReturnsZeroProgress()
    {
        _portMock.Setup(p => p.FindByName(It.IsAny<string>())).Returns((Port?)null);

        var shipment = _service.AddShipment("Test", "Unknown1", "Unknown2");
        var progress = _service.GetProgress(shipment.Id);

        progress.Should().NotBeNull();
        progress!.PercentComplete.Should().Be(0);
    }

    public void Dispose() => _service.Dispose();
}
