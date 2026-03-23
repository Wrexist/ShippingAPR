using FluentAssertions;
using ShippingAPR.Core.Calculations;

namespace ShippingAPR.Core.Tests;

public class BearingCalculatorTests
{
    [Fact]
    public void DueNorth_Returns0()
    {
        var bearing = BearingCalculator.InitialBearing(
            57.0, 12.0, 58.0, 12.0);

        bearing.Should().BeApproximately(0, 1);
    }

    [Fact]
    public void DueSouth_Returns180()
    {
        var bearing = BearingCalculator.InitialBearing(
            58.0, 12.0, 57.0, 12.0);

        bearing.Should().BeApproximately(180, 1);
    }

    [Fact]
    public void DueEast_Returns90()
    {
        var bearing = BearingCalculator.InitialBearing(
            57.0, 11.0, 57.0, 12.0);

        bearing.Should().BeApproximately(90, 5); // Not exactly 90 due to lat convergence
    }

    [Fact]
    public void GothenburgToStockholm_ReturnsNorthEast()
    {
        var bearing = BearingCalculator.InitialBearing(
            57.7089, 11.9746, 59.3293, 18.0686);

        // Should be roughly NE (30-60 degrees)
        bearing.Should().BeInRange(25, 65);
    }

    [Fact]
    public void AngleDifference_SameBearing_ReturnsZero()
    {
        BearingCalculator.AngleDifference(45, 45).Should().Be(0);
    }

    [Fact]
    public void AngleDifference_Opposite_Returns180()
    {
        BearingCalculator.AngleDifference(0, 180).Should().Be(180);
    }

    [Fact]
    public void AngleDifference_AcrossNorth_ReturnsSmallAngle()
    {
        // 350° to 10° should be 20°, not 340°
        BearingCalculator.AngleDifference(350, 10).Should().BeApproximately(20, 0.001);
    }

    [Fact]
    public void AngleDifference_IsSymmetric()
    {
        var d1 = BearingCalculator.AngleDifference(30, 60);
        var d2 = BearingCalculator.AngleDifference(60, 30);
        d1.Should().BeApproximately(d2, 0.001);
    }

    [Fact]
    public void BearingAlwaysInRange()
    {
        for (double lat = -80; lat <= 80; lat += 20)
        {
            for (double lon = -170; lon <= 170; lon += 40)
            {
                var bearing = BearingCalculator.InitialBearing(lat, lon, lat + 1, lon + 1);
                bearing.Should().BeInRange(0, 360);
            }
        }
    }
}
