using FluentAssertions;
using ShippingAPR.Infrastructure.Providers;
using Xunit;

namespace ShippingAPR.Infrastructure.Tests;

public class AisPositionNormalizerTests
{
    [Fact]
    public void TryCreate_NullIsland_ReturnsNull()
    {
        AisPositionNormalizer.TryCreate(0, 0, 5, 90, 90).Should().BeNull();
    }

    [Theory]
    [InlineData(91, 10)]
    [InlineData(-91, 10)]
    [InlineData(10, 181)]
    [InlineData(10, -181)]
    public void TryCreate_OutOfRange_ReturnsNull(double lat, double lon)
    {
        AisPositionNormalizer.TryCreate(lat, lon, 5, 90, 90).Should().BeNull();
    }

    [Fact]
    public void TryCreate_ValidFix_MapsValues()
    {
        var p = AisPositionNormalizer.TryCreate(57.6, 11.8, 12.5, 45, 50);

        p.Should().NotBeNull();
        p!.Latitude.Should().Be(57.6);
        p.Longitude.Should().Be(11.8);
        p.SpeedOverGround.Should().Be(12.5);
        p.CourseOverGround.Should().Be(45);
        p.TrueHeading.Should().Be(50);
    }

    [Fact]
    public void TryCreate_SpeedSentinel_NormalisedToZero()
    {
        AisPositionNormalizer.TryCreate(57.6, 11.8, 102.3, 45, 50)!.SpeedOverGround.Should().Be(0);
    }

    [Fact]
    public void TryCreate_CourseSentinel_NormalisedToZero_AndHeadingFallsBack()
    {
        var p = AisPositionNormalizer.TryCreate(57.6, 11.8, 5, 360, 0);
        p!.CourseOverGround.Should().Be(0);
        p.TrueHeading.Should().Be(0); // heading 0 (missing) falls back to normalised COG
    }

    [Fact]
    public void TryCreate_NegativeSpeed_ClampedToZero()
    {
        AisPositionNormalizer.TryCreate(57.6, 11.8, -1, 45, 50)!.SpeedOverGround.Should().Be(0);
    }
}
