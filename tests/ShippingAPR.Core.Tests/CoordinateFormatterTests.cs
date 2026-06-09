using FluentAssertions;
using ShippingAPR.Core.Formatting;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class CoordinateFormatterTests
{
    [Fact]
    public void Format_NorthEast_UsesNAndE()
    {
        CoordinateFormatter.Format(57.7089, 11.9746).Should().Be("57.7089°N, 11.9746°E");
    }

    [Fact]
    public void Format_SouthWest_UsesSAndW_WithAbsoluteValues()
    {
        // Off the coast near Buenos Aires.
        CoordinateFormatter.Format(-34.6037, -58.3816).Should().Be("34.6037°S, 58.3816°W");
    }

    [Fact]
    public void Format_SouthEast_Sydney()
    {
        CoordinateFormatter.Format(-33.8688, 151.2093).Should().Be("33.8688°S, 151.2093°E");
    }

    [Fact]
    public void Format_Equator_TreatedAsNorthEast()
    {
        CoordinateFormatter.Format(0, 0, decimals: 1).Should().Be("0.0°N, 0.0°E");
    }

    [Fact]
    public void Format_RespectsDecimals()
    {
        CoordinateFormatter.Format(57.7089, 11.9746, decimals: 2).Should().Be("57.71°N, 11.97°E");
    }
}
