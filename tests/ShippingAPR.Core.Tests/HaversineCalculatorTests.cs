using FluentAssertions;
using ShippingAPR.Core.Calculations;

namespace ShippingAPR.Core.Tests;

public class HaversineCalculatorTests
{
    [Fact]
    public void SamePoint_ReturnsZero()
    {
        var distance = HaversineCalculator.DistanceInNauticalMiles(
            57.7089, 11.9746, 57.7089, 11.9746);

        distance.Should().Be(0);
    }

    [Fact]
    public void GothenburgToStockholm_ReturnsCorrectDistance()
    {
        // Gothenburg (57.7089, 11.9746) to Stockholm (59.3293, 18.0686)
        // Approximately 220 NM by great circle
        var distance = HaversineCalculator.DistanceInNauticalMiles(
            57.7089, 11.9746, 59.3293, 18.0686);

        distance.Should().BeInRange(195, 225);
    }

    [Fact]
    public void GothenburgToCopenhagen_ReturnsCorrectDistance()
    {
        // Gothenburg (57.7089, 11.9746) to Copenhagen (55.6761, 12.5683)
        // Approximately 110 NM
        var distance = HaversineCalculator.DistanceInNauticalMiles(
            57.7089, 11.9746, 55.6761, 12.5683);

        distance.Should().BeInRange(100, 130);
    }

    [Fact]
    public void LongDistance_LondonToNewYork()
    {
        // London (51.5074, -0.1278) to New York (40.7128, -74.0060)
        // Approximately 3000 NM
        var distance = HaversineCalculator.DistanceInNauticalMiles(
            51.5074, -0.1278, 40.7128, -74.0060);

        distance.Should().BeInRange(2950, 3050);
    }

    [Fact]
    public void DistanceInKilometers_ConvertsCorrectly()
    {
        var nm = HaversineCalculator.DistanceInNauticalMiles(
            57.7089, 11.9746, 55.6761, 12.5683);
        var km = HaversineCalculator.DistanceInKilometers(
            57.7089, 11.9746, 55.6761, 12.5683);

        km.Should().BeApproximately(nm * 1.852, 0.1);
    }

    [Fact]
    public void SymmetricDistance()
    {
        var d1 = HaversineCalculator.DistanceInNauticalMiles(
            57.7089, 11.9746, 55.6761, 12.5683);
        var d2 = HaversineCalculator.DistanceInNauticalMiles(
            55.6761, 12.5683, 57.7089, 11.9746);

        d1.Should().BeApproximately(d2, 0.001);
    }
}
