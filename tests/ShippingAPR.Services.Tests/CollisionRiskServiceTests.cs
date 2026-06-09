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

    private IReadOnlyList<NotificationMessage> CollisionAlerts() =>
        _notificationService.History
            .Where(n => n.Type == NotificationType.CollisionRisk)
            .ToList();

    [Fact]
    public void Scan_NoVessels_PublishesNothing()
    {
        _sut.ScanForCollisionRisks();

        CollisionAlerts().Should().BeEmpty();
    }

    [Fact]
    public void Scan_SingleVessel_PublishesNothing()
    {
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 10, 90), null);

        _sut.ScanForCollisionRisks();

        CollisionAlerts().Should().BeEmpty();
    }

    [Fact]
    public void Scan_TwoDistantVessels_NoAlert()
    {
        // ~19 NM apart at this latitude — beyond the 5 NM pre-filter.
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 10, 90), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.7, 12.50, 10, 270), null);

        _sut.ScanForCollisionRisks();

        CollisionAlerts().Should().BeEmpty();
    }

    [Fact]
    public void Scan_TwoConvergingVessels_PublishesCollisionRisk()
    {
        // ~1.6 NM apart, head-on at 10 kn each → CPA ≈ 0, TCPA ≈ 5 min.
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 10, 90), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.7, 11.95, 10, 270), null);

        _sut.ScanForCollisionRisks();

        CollisionAlerts().Should().ContainSingle()
            .Which.Type.Should().Be(NotificationType.CollisionRisk);
    }

    [Fact]
    public void Scan_ConvergingButTooSlow_NoAlert()
    {
        // Both below the 0.5 kn "moving" threshold → filtered out before CPA.
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 0.3, 90), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.7, 11.95, 0.3, 270), null);

        _sut.ScanForCollisionRisks();

        CollisionAlerts().Should().BeEmpty();
    }

    [Fact]
    public void Scan_SamePairTwice_AlertsOnlyOnce()
    {
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 10, 90), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.7, 11.95, 10, 270), null);

        _sut.ScanForCollisionRisks();
        _sut.ScanForCollisionRisks();

        // De-duplicated via the active-risk tracking — still a single alert.
        CollisionAlerts().Should().ContainSingle();
    }

    [Fact]
    public void Scan_RiskClearsThenRecurs_AlertsAgain()
    {
        var clock = new FakeClock();
        _sut.Clock = clock;

        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 10, 90), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.7, 11.95, 10, 270), null);

        _sut.ScanForCollisionRisks(); // episode 1 → alert
        _sut.ScanForCollisionRisks(); // same episode → no new alert
        CollisionAlerts().Should().ContainSingle();

        // Pair stops converging (moving apart) and time passes beyond the clear window.
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 10, 270), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.7, 11.95, 10, 90), null);
        clock.Now = clock.Now.AddSeconds(61);
        _sut.ScanForCollisionRisks(); // no risk; stale episode expires

        // They converge again → a brand-new episode → a second alert.
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.90, 10, 90), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.7, 11.95, 10, 270), null);
        _sut.ScanForCollisionRisks();

        CollisionAlerts().Should().HaveCount(2);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
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
