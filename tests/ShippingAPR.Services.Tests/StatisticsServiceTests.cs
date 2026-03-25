using FluentAssertions;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class StatisticsServiceTests
{
    private readonly VesselStore _store = new();
    private readonly Mock<IWatchlistService> _watchlist = new();
    private readonly StatisticsService _sut;

    public StatisticsServiceTests()
    {
        _sut = new StatisticsService(_store, _watchlist.Object);
    }

    [Fact]
    public void EmptyStore_ReturnsZeroSnapshot()
    {
        var snap = _sut.GetSnapshot();

        snap.TotalVessels.Should().Be(0);
        snap.AverageSpeed.Should().Be(0);
        snap.VesselsUnderWay.Should().Be(0);
        snap.VesselsAtAnchor.Should().Be(0);
        snap.VesselsMoored.Should().Be(0);
        snap.TopDestinations.Should().BeEmpty();
    }

    [Fact]
    public void CountsByType_ReflectsVessels()
    {
        AddVessel(200000001, VesselType.Cargo, 10);
        AddVessel(200000002, VesselType.Cargo, 12);
        AddVessel(200000003, VesselType.Tanker, 8);

        var snap = _sut.GetSnapshot();

        snap.TotalVessels.Should().Be(3);
        snap.VesselCountByType[VesselType.Cargo].Should().Be(2);
        snap.VesselCountByType[VesselType.Tanker].Should().Be(1);
    }

    [Fact]
    public void AverageSpeed_IsCorrect()
    {
        AddVessel(200000001, VesselType.Cargo, 10);
        AddVessel(200000002, VesselType.Cargo, 20);

        var snap = _sut.GetSnapshot();

        snap.AverageSpeed.Should().Be(15);
    }

    [Fact]
    public void NavigationalStatusCounts_AreCorrect()
    {
        _store.AddOrUpdate(200000001, CreatePosition(10, NavigationalStatus.UnderWayUsingEngine), null);
        _store.AddOrUpdate(200000002, CreatePosition(0, NavigationalStatus.AtAnchor), null);
        _store.AddOrUpdate(200000003, CreatePosition(0, NavigationalStatus.Moored), null);

        var snap = _sut.GetSnapshot();

        snap.VesselsUnderWay.Should().Be(1);
        snap.VesselsAtAnchor.Should().Be(1);
        snap.VesselsMoored.Should().Be(1);
    }

    [Fact]
    public void TopDestinations_LimitedToFive()
    {
        for (int i = 0; i < 10; i++)
        {
            _store.AddOrUpdate(200000001 + i, CreatePosition(10, NavigationalStatus.UnderWayUsingEngine),
                new VesselStaticData { Destination = $"PORT_{i % 7}" });
        }

        var snap = _sut.GetSnapshot();

        snap.TopDestinations.Should().HaveCountLessOrEqualTo(5);
    }

    [Fact]
    public void WatchedVesselCount_ReflectsWatchlist()
    {
        _watchlist.Setup(w => w.IsWatched(200000001)).Returns(true);
        _watchlist.Setup(w => w.IsWatched(200000002)).Returns(false);

        AddVessel(200000001, VesselType.Cargo, 10);
        AddVessel(200000002, VesselType.Tanker, 10);

        var snap = _sut.GetSnapshot();

        snap.WatchedVesselCount.Should().Be(1);
    }

    private void AddVessel(int mmsi, VesselType type, double speed)
    {
        _store.AddOrUpdate(mmsi, CreatePosition(speed, NavigationalStatus.UnderWayUsingEngine),
            new VesselStaticData { ShipType = type });
    }

    private static VesselPosition CreatePosition(double speed, NavigationalStatus status)
    {
        return new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
            SpeedOverGround = speed,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = status
        };
    }
}
