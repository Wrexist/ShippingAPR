using FluentAssertions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class SpotlightServiceTests
{
    private readonly VesselStore _store = new();
    private readonly SpotlightService _sut;

    public SpotlightServiceTests()
    {
        _sut = new SpotlightService(_store);
    }

    [Fact]
    public void EmptyStore_ReturnsNull()
    {
        var (vessel, reason) = _sut.GetSpotlight();

        vessel.Should().BeNull();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void VesselsWithStaticData_ReturnsSpotlight()
    {
        _store.AddOrUpdate(200000001, CreatePosition(10),
            new VesselStaticData { ShipType = VesselType.Cargo, DimensionA = 100 });

        var (vessel, reason) = _sut.GetSpotlight();

        vessel.Should().NotBeNull();
        vessel!.Mmsi.Should().Be(200000001);
    }

    [Fact]
    public void LargeVessel_ScoresHigher()
    {
        _store.AddOrUpdate(200000001, CreatePosition(10),
            new VesselStaticData { ShipType = VesselType.Cargo, DimensionA = 50 });
        _store.AddOrUpdate(200000002, CreatePosition(10),
            new VesselStaticData { ShipType = VesselType.Cargo, DimensionA = 350 });

        var (vessel, reason) = _sut.GetSpotlight();

        vessel!.Mmsi.Should().Be(200000002);
        reason.Should().Contain("massive");
    }

    [Fact]
    public void FastVessel_ScoresHigher()
    {
        _store.AddOrUpdate(200000001, CreatePosition(5),
            new VesselStaticData { ShipType = VesselType.Cargo });
        _store.AddOrUpdate(200000002, CreatePosition(25),
            new VesselStaticData { ShipType = VesselType.Cargo });

        var (vessel, reason) = _sut.GetSpotlight();

        vessel!.Mmsi.Should().Be(200000002);
        reason.Should().Contain("speeding");
    }

    [Fact]
    public void MilitaryVessel_IsRare()
    {
        _store.AddOrUpdate(200000001, CreatePosition(10),
            new VesselStaticData { ShipType = VesselType.Cargo });
        _store.AddOrUpdate(200000002, CreatePosition(10),
            new VesselStaticData { ShipType = VesselType.Military });

        var (vessel, reason) = _sut.GetSpotlight();

        vessel!.Mmsi.Should().Be(200000002);
        reason.Should().Contain("rare");
    }

    [Fact]
    public void CachedSpotlight_ReturnsWithinHour()
    {
        _store.AddOrUpdate(200000001, CreatePosition(10),
            new VesselStaticData { ShipType = VesselType.Cargo });

        var first = _sut.GetSpotlight();
        var second = _sut.GetSpotlight();

        first.Vessel.Should().BeSameAs(second.Vessel);
    }

    private static VesselPosition CreatePosition(double speed)
    {
        return new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
            SpeedOverGround = speed,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = NavigationalStatus.UnderWayUsingEngine
        };
    }
}
