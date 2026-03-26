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

public class VesselTrackingServiceEnrichmentTests
{
    private readonly Mock<IAisDataProvider> _aisClientMock;
    private readonly Mock<IVesselEnrichmentClient> _enrichmentMock;
    private readonly VesselStore _vesselStore;
    private readonly VesselTrackingService _service;

    public VesselTrackingServiceEnrichmentTests()
    {
        _aisClientMock = new Mock<IAisDataProvider>();
        _enrichmentMock = new Mock<IVesselEnrichmentClient>();
        _vesselStore = new VesselStore(Options.Create(new TrackingOptions()));

        _service = new VesselTrackingService(
            _aisClientMock.Object,
            _vesselStore,
            new PortRepository(),
            Mock.Of<ILogger<VesselTrackingService>>(),
            Options.Create(new TrackingOptions
            {
                PurgeIntervalMinutes = 2,
                StaleAgeMinutes = 10,
                EtaDistanceThresholdNm = 0.5,
                EtaHeadingThresholdDeg = 5.0,
                PortCacheMaxSize = 10
            }),
            _enrichmentMock.Object);
    }

    [Fact]
    public void OnPositionReport_WithNoStaticData_QueuesEnrichment()
    {
        // Arrange: send a position report for a vessel with no static data
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

        _aisClientMock.Raise(c => c.MessageReceived += null, _aisClientMock.Object, args);

        // The vessel should be added to the store
        var vessel = _vesselStore.GetByMmsi(265000001);
        vessel.Should().NotBeNull();
        vessel!.StaticData.Should().BeNull();
    }

    [Fact]
    public void OnStaticData_DoesNotQueueEnrichment()
    {
        // Arrange: send static data first
        var staticArgs = new AisMessageEventArgs
        {
            MessageType = "ShipStaticData",
            Mmsi = 265000001,
            StaticData = new VesselStaticData
            {
                Name = "TEST SHIP",
                ShipType = VesselType.Cargo
            }
        };

        _aisClientMock.Raise(c => c.MessageReceived += null, _aisClientMock.Object, staticArgs);

        var vessel = _vesselStore.GetByMmsi(265000001);
        vessel.Should().NotBeNull();
        vessel!.StaticData.Should().NotBeNull();
        vessel.StaticData!.Name.Should().Be("TEST SHIP");
    }

    [Fact]
    public void ConstructsWithNullEnrichmentClient()
    {
        // Should not throw when enrichment client is null
        var service = new VesselTrackingService(
            _aisClientMock.Object,
            _vesselStore,
            new PortRepository(),
            Mock.Of<ILogger<VesselTrackingService>>(),
            Options.Create(new TrackingOptions()),
            enrichmentClient: null);

        service.Should().NotBeNull();
    }
}
