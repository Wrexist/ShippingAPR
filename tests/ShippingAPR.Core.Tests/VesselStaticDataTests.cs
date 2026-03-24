using FluentAssertions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class VesselStaticDataTests
{
    [Fact]
    public void LengthOverall_IsSumOfAAndB()
    {
        var data = new VesselStaticData { DimensionA = 150, DimensionB = 50 };
        data.LengthOverall.Should().Be(200);
    }

    [Fact]
    public void Beam_IsSumOfCAndD()
    {
        var data = new VesselStaticData { DimensionC = 20, DimensionD = 12 };
        data.Beam.Should().Be(32);
    }

    [Fact]
    public void ZeroDimensions_ReturnsZero()
    {
        var data = new VesselStaticData();
        data.LengthOverall.Should().Be(0);
        data.Beam.Should().Be(0);
    }

    [Fact]
    public void AllProperties_DefaultCorrectly()
    {
        var data = new VesselStaticData();
        data.Name.Should().BeNull();
        data.CallSign.Should().BeNull();
        data.ImoNumber.Should().Be(0);
        data.ShipType.Should().Be(VesselType.Unknown);
        data.Destination.Should().BeNull();
        data.ReportedEta.Should().BeNull();
        data.Draught.Should().Be(0);
        data.CountryCode.Should().BeNull();
    }

    [Fact]
    public void AllProperties_SetCorrectly()
    {
        var eta = new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        var data = new VesselStaticData
        {
            Name = "STENA GERMANICA",
            CallSign = "SBHI",
            ImoNumber = 9145176,
            ShipType = VesselType.Passenger,
            Destination = "SEGOT",
            ReportedEta = eta,
            Draught = 6.1,
            DimensionA = 140,
            DimensionB = 48,
            DimensionC = 15,
            DimensionD = 15,
            CountryCode = "SE"
        };

        data.Name.Should().Be("STENA GERMANICA");
        data.LengthOverall.Should().Be(188);
        data.Beam.Should().Be(30);
        data.Draught.Should().Be(6.1);
        data.CountryCode.Should().Be("SE");
    }
}
