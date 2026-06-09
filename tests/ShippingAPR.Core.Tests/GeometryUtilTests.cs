using FluentAssertions;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class GeometryUtilTests
{
    // A 10×10 square in (lat, lon).
    private static readonly GeoPoint[] Square =
    {
        new(0, 0), new(0, 10), new(10, 10), new(10, 0)
    };

    // An L-shape: full band lat 0–5 (lon 0–10) plus the left column lat 5–10 (lon 0–5);
    // the top-right quadrant (lat 5–10, lon 5–10) is the concave notch and lies outside.
    private static readonly GeoPoint[] LShape =
    {
        new(0, 0), new(0, 10), new(5, 10), new(5, 5), new(10, 5), new(10, 0)
    };

    [Fact]
    public void PointInside_ReturnsTrue() =>
        GeometryUtil.PointInPolygon(5, 5, Square).Should().BeTrue();

    [Theory]
    [InlineData(15, 5)]
    [InlineData(5, 15)]
    [InlineData(-1, 5)]
    [InlineData(5, -1)]
    public void PointOutside_ReturnsFalse(double lat, double lon) =>
        GeometryUtil.PointInPolygon(lat, lon, Square).Should().BeFalse();

    [Fact]
    public void FewerThanThreeVertices_ReturnsFalse() =>
        GeometryUtil.PointInPolygon(5, 5, new GeoPoint[] { new(0, 0), new(10, 10) }).Should().BeFalse();

    [Theory]
    [InlineData(2, 7, true)]   // bottom band
    [InlineData(7, 2, true)]   // left column
    [InlineData(7, 7, false)]  // concave notch — outside
    public void ConcavePolygon_RespectsNotch(double lat, double lon, bool expected) =>
        GeometryUtil.PointInPolygon(lat, lon, LShape).Should().Be(expected);
}
