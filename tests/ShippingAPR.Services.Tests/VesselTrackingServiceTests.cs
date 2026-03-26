using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.Ports;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class VesselTrackingServiceTests
{
    private readonly Mock<IAisDataProvider> _aisClientMock;
    private readonly VesselStore _vesselStore;
    private readonly PortRepository _portRepository;
    private readonly VesselTrackingService _service;
    private readonly TrackingOptions _options;

    public VesselTrackingServiceTests()
    {
        _aisClientMock = new Mock<IAisDataProvider>();
        _vesselStore = new VesselStore(Options.Create(new TrackingOptions()));
        _portRepository = new PortRepository();
        _options = new TrackingOptions
        {
            PurgeIntervalMinutes = 2,
            StaleAgeMinutes = 10,
            EtaDistanceThresholdNm = 0.5,
            EtaHeadingThresholdDeg = 5.0,
            PortCacheMaxSize = 10 // small for testing
        };

        _service = new VesselTrackingService(
            _aisClientMock.Object,
            _vesselStore,
            _portRepository,
            Mock.Of<ILogger<VesselTrackingService>>(),
            Options.Create(_options));
    }

    [Fact]
    public async Task StartTrackingAsync_CallsAisClientConnect()
    {
        var area = BoundingBox.GothenburgDefault;
        _aisClientMock.Setup(c => c.ConnectAsync(area, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.StartTrackingAsync(area);

        _aisClientMock.Verify(c => c.ConnectAsync(area, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopTrackingAsync_CallsAisClientDisconnect()
    {
        _aisClientMock.Setup(c => c.DisconnectAsync()).Returns(Task.CompletedTask);

        await _service.StopTrackingAsync();

        _aisClientMock.Verify(c => c.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task ChangeAreaAsync_CallsUpdateSubscription()
    {
        var area = BoundingBox.NorthernEuropeDefault;
        _aisClientMock.Setup(c => c.UpdateSubscriptionAsync(area, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.ChangeAreaAsync(area);

        _aisClientMock.Verify(c => c.UpdateSubscriptionAsync(area, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void OnMessageReceived_PositionReport_UpdatesVesselStore()
    {
        var args = new AisMessageEventArgs
        {
            MessageType = "PositionReport",
            Mmsi = 265000001,
            Position = new VesselPosition
            {
                Latitude = 57.7,
                Longitude = 11.9,
                SpeedOverGround = 12.5,
                CourseOverGround = 45
            }
        };

        // Trigger the event
        _aisClientMock.Raise(c => c.MessageReceived += null, _aisClientMock.Object, args);

        var vessel = _vesselStore.GetByMmsi(265000001);
        vessel.Should().NotBeNull();
        vessel!.CurrentPosition!.Latitude.Should().Be(57.7);
    }

    [Fact]
    public void OnMessageReceived_StaticData_UpdatesVesselStore()
    {
        var args = new AisMessageEventArgs
        {
            MessageType = "ShipStaticData",
            Mmsi = 265000001,
            StaticData = new VesselStaticData
            {
                Name = "TEST SHIP",
                ShipType = VesselType.Cargo
            }
        };

        _aisClientMock.Raise(c => c.MessageReceived += null, _aisClientMock.Object, args);

        var vessel = _vesselStore.GetByMmsi(265000001);
        vessel.Should().NotBeNull();
        vessel!.StaticData!.Name.Should().Be("TEST SHIP");
    }

    [Fact]
    public void OnMessageReceived_WithDestinationAndPosition_CalculatesEta()
    {
        // First add static data with a known destination (Gothenburg)
        _aisClientMock.Raise(c => c.MessageReceived += null, _aisClientMock.Object, new AisMessageEventArgs
        {
            MessageType = "ShipStaticData",
            Mmsi = 265000001,
            StaticData = new VesselStaticData
            {
                Name = "TEST SHIP",
                Destination = "SEGOT"
            }
        });

        // Then send a position report from nearby
        _aisClientMock.Raise(c => c.MessageReceived += null, _aisClientMock.Object, new AisMessageEventArgs
        {
            MessageType = "PositionReport",
            Mmsi = 265000001,
            Position = new VesselPosition
            {
                Latitude = 57.5,
                Longitude = 11.5,
                SpeedOverGround = 15.0,
                CourseOverGround = 45,
                TrueHeading = 45
            }
        });

        var vessel = _vesselStore.GetByMmsi(265000001);
        vessel!.CalculatedEta.Should().NotBeNull();
        vessel.CalculatedEta!.DistanceNauticalMiles.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConnectionStatus_DelegatesFromAisClient()
    {
        _aisClientMock.SetupGet(c => c.Status).Returns(ConnectionStatus.Connected);
        _service.ConnectionStatus.Should().Be(ConnectionStatus.Connected);
    }

    [Fact]
    public void ConnectionStatusChanged_ForwardedFromAisClient()
    {
        ConnectionStatus? receivedStatus = null;
        _service.ConnectionStatusChanged += (_, status) => receivedStatus = status;

        _aisClientMock.Raise(c => c.ConnectionStatusChanged += null,
            _aisClientMock.Object, ConnectionStatus.Connected);

        receivedStatus.Should().Be(ConnectionStatus.Connected);
    }
}
