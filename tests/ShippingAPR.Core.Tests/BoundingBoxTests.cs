using FluentAssertions;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class BoundingBoxTests
{
    [Fact]
    public void Constructor_ValidCoordinates_Creates()
    {
        var box = new BoundingBox(10, 20, 30, 40);

        box.MinLatitude.Should().Be(10);
        box.MinLongitude.Should().Be(20);
        box.MaxLatitude.Should().Be(30);
        box.MaxLongitude.Should().Be(40);
    }

    [Fact]
    public void Constructor_MinLatGreaterThanMax_Throws()
    {
        var act = () => new BoundingBox(50, 20, 30, 40);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_MinLonGreaterThanMax_Throws()
    {
        var act = () => new BoundingBox(10, 50, 30, 40);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EqualMinMax_Allowed()
    {
        var box = new BoundingBox(10, 20, 10, 20);
        box.MinLatitude.Should().Be(box.MaxLatitude);
    }

    [Fact]
    public void Contains_PointInside_ReturnsTrue()
    {
        var box = new BoundingBox(10, 20, 30, 40);
        box.Contains(20, 30).Should().BeTrue();
    }

    [Fact]
    public void Contains_PointOutside_ReturnsFalse()
    {
        var box = new BoundingBox(10, 20, 30, 40);
        box.Contains(5, 30).Should().BeFalse();
        box.Contains(35, 30).Should().BeFalse();
        box.Contains(20, 15).Should().BeFalse();
        box.Contains(20, 45).Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 20)]  // min corner
    [InlineData(30, 40)]  // max corner
    [InlineData(10, 30)]  // min lat edge
    [InlineData(30, 30)]  // max lat edge
    [InlineData(20, 20)]  // min lon edge
    [InlineData(20, 40)]  // max lon edge
    public void Contains_PointOnBoundary_ReturnsTrue(double lat, double lon)
    {
        var box = new BoundingBox(10, 20, 30, 40);
        box.Contains(lat, lon).Should().BeTrue();
    }

    [Fact]
    public void ToAisStreamFormat_ReturnsCorrectArray()
    {
        var box = new BoundingBox(57.65, 11.80, 57.75, 12.05);
        var result = box.ToAisStreamFormat();

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new[] { 57.65, 11.80 });
        result[1].Should().BeEquivalentTo(new[] { 57.75, 12.05 });
    }

    [Fact]
    public void StaticPresets_AllValid()
    {
        var presets = new[]
        {
            BoundingBox.GothenburgDefault,
            BoundingBox.NorthernEuropeDefault,
            BoundingBox.Europe,
            BoundingBox.Asia,
            BoundingBox.Americas,
            BoundingBox.Global
        };

        foreach (var box in presets)
        {
            box.MinLatitude.Should().BeLessThanOrEqualTo(box.MaxLatitude);
            box.MinLongitude.Should().BeLessThanOrEqualTo(box.MaxLongitude);
        }
    }

    [Fact]
    public void WorldRegions_AllValid()
    {
        var regions = BoundingBox.WorldRegions;
        regions.Should().NotBeEmpty();

        foreach (var box in regions)
        {
            box.MinLatitude.Should().BeLessThanOrEqualTo(box.MaxLatitude);
            box.MinLongitude.Should().BeLessThanOrEqualTo(box.MaxLongitude);
        }
    }
}
