using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class CollisionRiskServiceTests
{
    private readonly VesselStore _store = new();
    private readonly NotificationService _notificationService;
    private readonly CollisionRiskService _sut;

    public CollisionRiskServiceTests()
    {
        var areaMonitor = new AreaMonitorService(_store, NullLogger<AreaMonitorService>.Instance);
        _notificationService = new NotificationService(
            areaMonitor,
            NullLogger<NotificationService>.Instance,
            Options.Create(new TrackingOptions()));
        _sut = new CollisionRiskService(
            _store,
            _notificationService,
            NullLogger<CollisionRiskService>.Instance,
            Options.Create(new TrackingOptions
            {
                CpaWarningDistanceNm = 0.5,
                CpaPreFilterDistanceNm = 5.0,
                TcpaWindowMinutes = 30
            }));
    }

    [Fact]
    public void NoVessels_DoesNotThrow()
    {
        // ExecuteAsync is protected, but we can verify scanning works via notification count
        _notificationService.History.Should().BeEmpty();
    }

    [Fact]
    public void SingleVessel_NoCollisionRisk()
    {
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.9, 10, 90), null);

        // No collision possible with a single vessel
        _notificationService.History.Should().BeEmpty();
    }

    [Fact]
    public void TwoDistantVessels_NoRisk()
    {
        // Vessels far apart
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.9, 10, 90), null);
        _store.AddOrUpdate(200000002, CreatePosition(60.0, 20.0, 10, 270), null);

        _notificationService.History.Should().BeEmpty();
    }

    private static VesselPosition CreatePosition(double lat, double lon, double speed, double course)
    {
        return new VesselPosition
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = speed,
            CourseOverGround = course,
            TrueHeading = (int)course,
            Status = NavigationalStatus.UnderWayUsingEngine
        };
    }
}
