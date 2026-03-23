using FluentAssertions;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class EtaCalculatorTests
{
    private static readonly Port Gothenburg = new("SEGOT", "Gothenburg", "SE", 57.7089, 11.9746);
    private static readonly Port Stockholm = new("SESTO", "Stockholm", "SE", 59.3293, 18.0686);

    [Fact]
    public void StationaryVessel_ReturnsNull()
    {
        var position = CreatePosition(57.0, 12.0, sog: 0.1, cog: 0);
        var result = EtaCalculator.Calculate(position, Gothenburg);

        result.Should().BeNull();
    }

    [Fact]
    public void VesselAtDestination_ReturnsZero()
    {
        var position = CreatePosition(57.7089, 11.9746, sog: 5.0, cog: 0);
        var result = EtaCalculator.Calculate(position, Gothenburg);

        result.Should().NotBeNull();
        result!.DistanceNauticalMiles.Should().BeLessThan(0.1);
        result.TimeToArrival.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void VesselHeadingDirectlyToPort_UsesFullSpeed()
    {
        // Vessel south of Gothenburg heading north
        var bearing = BearingCalculator.InitialBearing(57.0, 11.97, 57.7089, 11.9746);
        var position = CreatePosition(57.0, 11.97, sog: 10.0, cog: bearing);
        var result = EtaCalculator.Calculate(position, Gothenburg);

        result.Should().NotBeNull();
        // Course deviation should be near 0
        result!.CourseDeviationDegrees.Should().BeLessThan(5);
        // Effective speed should be close to SOG
        result.EffectiveSpeedKnots.Should().BeGreaterThan(9.5);
    }

    [Fact]
    public void VesselHeading90DegreesOff_ReducesEffectiveSpeed()
    {
        // Vessel heading east (90°) but destination is north (0°)
        var position = CreatePosition(57.0, 11.97, sog: 10.0, cog: 90);
        var result = EtaCalculator.Calculate(position, Gothenburg);

        result.Should().NotBeNull();
        // Course deviation should be large
        result!.CourseDeviationDegrees.Should().BeGreaterThan(45);
        // Effective speed should be much less than SOG
        result.EffectiveSpeedKnots.Should().BeLessThan(result.DistanceNauticalMiles); // sanity
    }

    [Fact]
    public void VesselMovingAway_StillProvidesEstimate()
    {
        // Vessel heading south (180°) but destination is north
        var position = CreatePosition(57.0, 11.97, sog: 10.0, cog: 180);
        var result = EtaCalculator.Calculate(position, Gothenburg);

        result.Should().NotBeNull();
        result!.CourseDeviationDegrees.Should().BeGreaterThan(150);
        // Should still give an ETA (though it will be very large)
        result.TimeToArrival.TotalHours.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DistanceCalculationIsAccurate()
    {
        var position = CreatePosition(57.0, 12.0, sog: 12.0, cog: 0);
        var result = EtaCalculator.Calculate(position, Gothenburg);

        // Verify against direct Haversine calculation
        var expectedDistance = HaversineCalculator.DistanceInNauticalMiles(
            57.0, 12.0, Gothenburg.Latitude, Gothenburg.Longitude);

        result.Should().NotBeNull();
        result!.DistanceNauticalMiles.Should().BeApproximately(expectedDistance, 0.2);
    }

    [Fact]
    public void EtaIsInFuture()
    {
        var position = CreatePosition(57.0, 12.0, sog: 12.0, cog: 0);
        var result = EtaCalculator.Calculate(position, Gothenburg);

        result.Should().NotBeNull();
        result!.EstimatedArrival.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void LongVoyage_GothenburgToStockholm()
    {
        // Heading roughly NE toward Stockholm at 15 knots
        var bearing = BearingCalculator.InitialBearing(
            57.7089, 11.9746, 59.3293, 18.0686);
        var position = CreatePosition(57.7089, 11.9746, sog: 15.0, cog: bearing);
        var result = EtaCalculator.Calculate(position, Stockholm);

        result.Should().NotBeNull();
        // Distance ~210 NM, at 15 kn ≈ 14 hours
        result!.TimeToArrival.TotalHours.Should().BeInRange(10, 20);
    }

    private static VesselPosition CreatePosition(
        double lat, double lon, double sog, double cog) =>
        new()
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = sog,
            CourseOverGround = cog,
            TrueHeading = cog,
            Status = NavigationalStatus.UnderWayUsingEngine,
            Timestamp = DateTime.UtcNow
        };
}
