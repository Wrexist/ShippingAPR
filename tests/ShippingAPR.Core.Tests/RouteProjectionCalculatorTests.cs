using FluentAssertions;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class RouteProjectionCalculatorTests
{
    [Fact]
    public void StationaryVessel_ReturnsEmpty()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.7, Longitude = 11.9,
            SpeedOverGround = 0.1, CourseOverGround = 90
        };

        var result = RouteProjectionCalculator.Project(pos);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ImplausibleSpeed_ReturnsEmpty()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.7, Longitude = 11.9,
            SpeedOverGround = 55.0, CourseOverGround = 90
        };

        var result = RouteProjectionCalculator.Project(pos);

        result.Should().BeEmpty();
    }

    [Fact]
    public void MovingVessel_ReturnsCorrectNumberOfPoints()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.7, Longitude = 11.9,
            SpeedOverGround = 12.0, CourseOverGround = 90
        };

        var result = RouteProjectionCalculator.Project(pos, horizonMinutes: 60, stepCount: 12);

        result.Should().HaveCount(12);
    }

    [Fact]
    public void DueNorth_LatitudeIncreases()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.0, Longitude = 12.0,
            SpeedOverGround = 10.0, CourseOverGround = 0
        };

        var result = RouteProjectionCalculator.Project(pos, horizonMinutes: 60, stepCount: 4);

        result.Should().NotBeEmpty();
        foreach (var point in result)
        {
            point.Latitude.Should().BeGreaterThan(57.0);
            point.Longitude.Should().BeApproximately(12.0, 0.1);
        }
    }

    [Fact]
    public void DueEast_LongitudeIncreases()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.0, Longitude = 12.0,
            SpeedOverGround = 10.0, CourseOverGround = 90
        };

        var result = RouteProjectionCalculator.Project(pos, horizonMinutes: 60, stepCount: 4);

        result.Should().NotBeEmpty();
        foreach (var point in result)
        {
            point.Longitude.Should().BeGreaterThan(12.0);
        }
    }

    [Fact]
    public void DueSouth_LatitudeDecreases()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.0, Longitude = 12.0,
            SpeedOverGround = 10.0, CourseOverGround = 180
        };

        var result = RouteProjectionCalculator.Project(pos, horizonMinutes: 60, stepCount: 4);

        result.Should().NotBeEmpty();
        foreach (var point in result)
        {
            point.Latitude.Should().BeLessThan(57.0);
        }
    }

    [Fact]
    public void ProjectedPoints_AreProgressivelyFarther()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.0, Longitude = 12.0,
            SpeedOverGround = 15.0, CourseOverGround = 45
        };

        var result = RouteProjectionCalculator.Project(pos, horizonMinutes: 60, stepCount: 6);

        result.Should().HaveCount(6);

        // Each point should be farther from origin than the previous
        var prevDist = 0.0;
        foreach (var point in result)
        {
            var dist = HaversineCalculator.DistanceInNauticalMiles(57.0, 12.0, point.Latitude, point.Longitude);
            dist.Should().BeGreaterThan(prevDist);
            prevDist = dist;
        }
    }

    [Fact]
    public void DistanceMatchesSpeedAndTime()
    {
        var pos = new VesselPosition
        {
            Latitude = 0.0, Longitude = 0.0,
            SpeedOverGround = 10.0, CourseOverGround = 0
        };

        // 10 knots for 60 minutes = 10 nautical miles
        var result = RouteProjectionCalculator.Project(pos, horizonMinutes: 60, stepCount: 1);

        result.Should().HaveCount(1);
        var endDist = HaversineCalculator.DistanceInNauticalMiles(0, 0, result[0].Latitude, result[0].Longitude);
        endDist.Should().BeApproximately(10.0, 0.1);
    }

    [Fact]
    public void LongitudeWraps_NearAntimeridian()
    {
        var pos = new VesselPosition
        {
            Latitude = 0.0, Longitude = 179.5,
            SpeedOverGround = 20.0, CourseOverGround = 90
        };

        var result = RouteProjectionCalculator.Project(pos, horizonMinutes: 120, stepCount: 4);

        result.Should().NotBeEmpty();
        // Should wrap to negative longitude
        result.Last().Longitude.Should().BeLessThan(180.0);
        result.Last().Longitude.Should().BeGreaterThan(-180.0);
    }
}
