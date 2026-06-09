using System.Globalization;
using System.Windows;
using FluentAssertions;
using ShippingAPR.App.Converters;
using Xunit;

namespace ShippingAPR.App.Tests;

public class PercentToStarConverterTests
{
    private readonly PercentToStarConverter _c = new();

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(50.0, 50.0)]
    [InlineData(100.0, 100.0)]
    [InlineData(150.0, 100.0)] // clamped to 100
    [InlineData(-10.0, 0.0)]   // clamped to 0
    public void Convert_Fill_ReturnsClampedStar(double input, double expected)
    {
        var gl = (GridLength)_c.Convert(input, typeof(GridLength), null, CultureInfo.InvariantCulture);
        gl.IsStar.Should().BeTrue();
        gl.Value.Should().Be(expected);
    }

    [Fact]
    public void Convert_Remainder_Returns100MinusPercent()
    {
        var gl = (GridLength)_c.Convert(30.0, typeof(GridLength), "remainder", CultureInfo.InvariantCulture);
        gl.Value.Should().Be(70.0);
    }

    [Fact]
    public void Convert_NonNumeric_ReturnsZeroStar()
    {
        var gl = (GridLength)_c.Convert(null, typeof(GridLength), null, CultureInfo.InvariantCulture);
        gl.Value.Should().Be(0.0);
    }
}
