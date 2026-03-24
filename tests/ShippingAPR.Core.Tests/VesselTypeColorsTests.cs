using FluentAssertions;
using ShippingAPR.Core;
using ShippingAPR.Core.Enums;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class VesselTypeColorsTests
{
    [Theory]
    [InlineData(VesselType.Cargo, 76, 175, 80)]
    [InlineData(VesselType.Tanker, 255, 87, 34)]
    [InlineData(VesselType.Passenger, 33, 150, 243)]
    [InlineData(VesselType.Fishing, 255, 152, 0)]
    [InlineData(VesselType.SearchAndRescue, 244, 67, 54)]
    [InlineData(VesselType.Unknown, 158, 158, 158)]
    public void GetRgb_ReturnsExpectedColors(VesselType type, byte expectedR, byte expectedG, byte expectedB)
    {
        var (r, g, b) = VesselTypeColors.GetRgb(type);

        r.Should().Be(expectedR);
        g.Should().Be(expectedG);
        b.Should().Be(expectedB);
    }

    [Fact]
    public void GetRgb_AllDefinedTypes_ReturnNonDefault()
    {
        var definedTypes = new[]
        {
            VesselType.Cargo, VesselType.Tanker, VesselType.Passenger,
            VesselType.Fishing, VesselType.Tug, VesselType.Pilot,
            VesselType.Military, VesselType.Sailing, VesselType.PleasureCraft,
            VesselType.HighSpeedCraft, VesselType.SearchAndRescue
        };

        var defaultColor = VesselTypeColors.GetRgb(VesselType.Unknown);

        foreach (var type in definedTypes)
        {
            VesselTypeColors.GetRgb(type).Should().NotBe(defaultColor,
                because: $"{type} should have its own color");
        }
    }
}
