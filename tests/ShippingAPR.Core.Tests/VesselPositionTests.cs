using FluentAssertions;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class VesselPositionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(57.7089, 11.9746)]
    public void ValidCoordinates_Accepted(double lat, double lon)
    {
        var pos = new VesselPosition { Latitude = lat, Longitude = lon };
        pos.Latitude.Should().Be(lat);
        pos.Longitude.Should().Be(lon);
    }

    [Theory]
    [InlineData(-90.1)]
    [InlineData(90.1)]
    [InlineData(-200)]
    [InlineData(200)]
    public void InvalidLatitude_Throws(double lat)
    {
        var act = () => new VesselPosition { Latitude = lat, Longitude = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-180.1)]
    [InlineData(180.1)]
    [InlineData(-360)]
    [InlineData(360)]
    public void InvalidLongitude_Throws(double lon)
    {
        var act = () => new VesselPosition { Latitude = 0, Longitude = lon };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultTimestamp_IsApproximatelyNow()
    {
        var before = DateTime.UtcNow;
        var pos = new VesselPosition { Latitude = 0, Longitude = 0 };
        var after = DateTime.UtcNow;

        pos.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void AllProperties_SetCorrectly()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
            SpeedOverGround = 12.5,
            CourseOverGround = 45.0,
            TrueHeading = 43.0,
            Status = Core.Enums.NavigationalStatus.UnderWayUsingEngine,
            RateOfTurn = -2.5
        };

        pos.SpeedOverGround.Should().Be(12.5);
        pos.CourseOverGround.Should().Be(45.0);
        pos.TrueHeading.Should().Be(43.0);
        pos.Status.Should().Be(Core.Enums.NavigationalStatus.UnderWayUsingEngine);
        pos.RateOfTurn.Should().Be(-2.5);
    }
}
