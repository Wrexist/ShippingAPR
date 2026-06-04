using FluentAssertions;
using ShippingAPR.Core.Calculations;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class CpaCalculatorTests
{
    // Geometry note: the calculator uses an equirectangular projection around the
    // midpoint latitude. Placing vessels on the equator (lat 0) makes cosLat = 1, so
    // 1° longitude == 60 NM and the expected results are exact and easy to verify.

    [Fact]
    public void Calculate_HeadOnConvergingVessels_ReturnsZeroCpa()
    {
        // V1 at (0,0) heading east 10 kn; V2 12 NM (0.2°) east heading west 10 kn.
        var result = CpaCalculator.Calculate(
            1, 0, 0.0, 90, 10,
            2, 0, 0.2, 270, 10);

        result.Should().NotBeNull();
        result!.Mmsi1.Should().Be(1);
        result.Mmsi2.Should().Be(2);
        result.CpaNauticalMiles.Should().BeApproximately(0, 0.001);
        result.TimeToCpa.TotalHours.Should().BeApproximately(0.6, 0.001); // 12 NM / 20 kn closing
    }

    [Fact]
    public void Calculate_OffsetCrossingTracks_ReturnsNonZeroCpa()
    {
        // V1 at (0,0) east 10 kn; V2 at (0.05°N, 0.2°E) west 10 kn — offset by 3 NM in y.
        var result = CpaCalculator.Calculate(
            1, 0, 0.0, 90, 10,
            2, 0.05, 0.2, 270, 10);

        result.Should().NotBeNull();
        result!.CpaNauticalMiles.Should().BeApproximately(3.0, 0.01); // perpendicular offset
        result.TimeToCpa.TotalHours.Should().BeApproximately(0.6, 0.001);
    }

    [Fact]
    public void Calculate_HeadOnAcrossAntiMeridian_ReturnsZeroCpa()
    {
        // V1 at lon 179.95° heading east, V2 at lon -179.95° heading west — only
        // 0.1° (6 NM) apart across the date line, closing head-on. Without
        // longitude-difference normalisation this would read as ~360° apart and
        // hide the risk.
        var result = CpaCalculator.Calculate(
            1, 0, 179.95, 90, 10,
            2, 0, -179.95, 270, 10);

        result.Should().NotBeNull();
        result!.CpaNauticalMiles.Should().BeApproximately(0, 0.001);
        result.TimeToCpa.TotalHours.Should().BeApproximately(0.3, 0.001); // 6 NM / 20 kn
    }

    [Fact]
    public void Calculate_DivergingVessels_ReturnsNull()
    {
        // Same start positions but moving apart — CPA already passed (TCPA < 0).
        var result = CpaCalculator.Calculate(
            1, 0, 0.0, 270, 10,
            2, 0, 0.2, 90, 10);

        result.Should().BeNull();
    }

    [Fact]
    public void Calculate_ParallelSameCourseAndSpeed_ReturnsNull()
    {
        // Both heading east at the same speed — no relative motion, never converge.
        var result = CpaCalculator.Calculate(
            1, 0, 0.0, 90, 10,
            2, 0, 0.2, 90, 10);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(0.0, 10.0)] // first vessel stationary
    [InlineData(10.0, 0.0)] // second vessel stationary
    [InlineData(0.0, 0.0)]  // both stationary
    public void Calculate_StationaryVessel_ReturnsNull(double sog1, double sog2)
    {
        var result = CpaCalculator.Calculate(
            1, 0, 0.0, 90, sog1,
            2, 0, 0.2, 270, sog2);

        result.Should().BeNull();
    }

    [Fact]
    public void Calculate_BelowMinSpeedThreshold_ReturnsNull()
    {
        // 0.4 kn is below the 0.5 kn "moving" threshold.
        var result = CpaCalculator.Calculate(
            1, 0, 0.0, 90, 0.4,
            2, 0, 0.2, 270, 0.4);

        result.Should().BeNull();
    }
}
