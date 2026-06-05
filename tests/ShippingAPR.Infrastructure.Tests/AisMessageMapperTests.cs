using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Infrastructure.AisStream.Messages;
using ShippingAPR.Infrastructure.Mapping;
using Xunit;

namespace ShippingAPR.Infrastructure.Tests;

public class AisMessageMapperTests
{
    private readonly AisMessageMapper _mapper;

    public AisMessageMapperTests()
    {
        _mapper = new AisMessageMapper(Mock.Of<ILogger<AisMessageMapper>>());
    }

    private static AisMessage CreatePositionReportMessage(
        int mmsi = 265000001,
        double lat = 57.7,
        double lon = 11.9,
        double sog = 12.5,
        double cog = 45.0,
        int trueHeading = 43,
        int navStatus = 0,
        double rateOfTurn = 0)
    {
        var json = JsonSerializer.Serialize(new
        {
            PositionReport = new
            {
                UserID = mmsi,
                Sog = sog,
                Cog = cog,
                TrueHeading = trueHeading,
                Latitude = lat,
                Longitude = lon,
                NavigationalStatus = navStatus,
                RateOfTurn = rateOfTurn,
                Timestamp = 0,
                PositionAccuracy = false,
                Raim = false
            }
        });

        return new AisMessage
        {
            MessageType = "PositionReport",
            Message = JsonDocument.Parse(json).RootElement,
            MetaData = new AisMetaData
            {
                Mmsi = mmsi,
                Latitude = lat,
                Longitude = lon
            }
        };
    }

    private static AisMessage CreateStaticDataMessage(
        int mmsi = 265000001,
        string? name = "STENA GERMANICA",
        string? callSign = "SBHI",
        int imoNumber = 9145176,
        string? destination = "SEGOT",
        int type = 60,
        double draught = 61,  // in decimeters
        int dimA = 140, int dimB = 48, int dimC = 15, int dimD = 15,
        int etaMonth = 0, int etaDay = 0, int etaHour = 0, int etaMinute = 0)
    {
        var json = JsonSerializer.Serialize(new
        {
            ShipStaticData = new
            {
                UserID = mmsi,
                Name = name,
                CallSign = callSign,
                ImoNumber = imoNumber,
                Destination = destination,
                Type = type,
                MaximumStaticDraught = draught,
                Dimension = new { A = dimA, B = dimB, C = dimC, D = dimD },
                Eta = new { Month = etaMonth, Day = etaDay, Hour = etaHour, Minute = etaMinute }
            }
        });

        return new AisMessage
        {
            MessageType = "ShipStaticData",
            Message = JsonDocument.Parse(json).RootElement,
            MetaData = new AisMetaData { Mmsi = mmsi }
        };
    }

    // --- Position Report Tests ---

    [Fact]
    public void MapPositionReport_ValidData_MapsCorrectly()
    {
        var msg = CreatePositionReportMessage();
        var result = _mapper.Map(msg);

        result.Should().NotBeNull();
        result!.MessageType.Should().Be("PositionReport");
        result.Mmsi.Should().Be(265000001);
        result.Position.Should().NotBeNull();
        result.Position!.Latitude.Should().Be(57.7);
        result.Position.Longitude.Should().Be(11.9);
        result.Position.SpeedOverGround.Should().Be(12.5);
        result.Position.CourseOverGround.Should().Be(45.0);
        result.Position.TrueHeading.Should().Be(43);
    }

    [Fact]
    public void MapPositionReport_TrueHeading511_ReplacedWithCog()
    {
        var msg = CreatePositionReportMessage(trueHeading: 511, cog: 135.5);
        var result = _mapper.Map(msg);

        result!.Position!.TrueHeading.Should().Be(135.5);
    }

    [Fact]
    public void MapPositionReport_NavigationalStatusClamped()
    {
        var msg = CreatePositionReportMessage(navStatus: 20);
        var result = _mapper.Map(msg);

        ((int)result!.Position!.Status).Should().BeLessThanOrEqualTo(15);
    }

    [Fact]
    public void MapPositionReport_UsesMetaDataMmsi()
    {
        var msg = CreatePositionReportMessage(mmsi: 111111111);
        msg.MetaData!.Mmsi = 222222222;
        var result = _mapper.Map(msg);

        result!.Mmsi.Should().Be(222222222);
    }

    [Fact]
    public void MapPositionReport_NoMetaData_FallsBackToUserId()
    {
        var msg = CreatePositionReportMessage(mmsi: 333333333);
        msg.MetaData = null;
        var result = _mapper.Map(msg);

        result!.Mmsi.Should().Be(333333333);
    }

    [Fact]
    public void MapPositionReport_NullInnerReport_ReturnsNull()
    {
        var json = JsonSerializer.Serialize(new { PositionReport = (object?)null });
        var msg = new AisMessage
        {
            MessageType = "PositionReport",
            Message = JsonDocument.Parse(json).RootElement,
            MetaData = new AisMetaData { Mmsi = 123 }
        };

        _mapper.Map(msg).Should().BeNull();
    }

    // --- AIS Sentinel / "not available" values ---

    [Fact]
    public void MapPositionReport_SentinelLatitude91_ReturnsNull()
    {
        // lat=91 is the AIS "position not available" sentinel — must not be plotted.
        var msg = CreatePositionReportMessage(lat: 91.0);
        _mapper.Map(msg).Should().BeNull();
    }

    [Fact]
    public void MapPositionReport_SentinelLongitude181_ReturnsNull()
    {
        // lon=181 is the AIS "position not available" sentinel — must not be plotted.
        var msg = CreatePositionReportMessage(lon: 181.0);
        _mapper.Map(msg).Should().BeNull();
    }

    [Fact]
    public void MapPositionReport_BoundaryLatLon_StillMapped()
    {
        // Exact bounds (±90 / ±180) are valid positions, not sentinels.
        var msg = CreatePositionReportMessage(lat: 90.0, lon: 180.0);
        var result = _mapper.Map(msg);

        result.Should().NotBeNull();
        result!.Position!.Latitude.Should().Be(90.0);
        result.Position.Longitude.Should().Be(180.0);
    }

    [Fact]
    public void MapPositionReport_SpeedNotAvailable_NormalisedToZero()
    {
        // SOG 102.3 (raw 1023) means "not available" — must not be shown as 102.3 kn.
        var msg = CreatePositionReportMessage(sog: 102.3);
        var result = _mapper.Map(msg);

        result!.Position!.SpeedOverGround.Should().Be(0);
    }

    [Fact]
    public void MapPositionReport_CourseNotAvailable_NormalisedToZero()
    {
        // COG 360.0 (raw 3600) means "not available".
        var msg = CreatePositionReportMessage(cog: 360.0);
        var result = _mapper.Map(msg);

        result!.Position!.CourseOverGround.Should().Be(0);
    }

    [Fact]
    public void MapPositionReport_CourseNotAvailable_HeadingFallbackAlsoNormalised()
    {
        // TrueHeading=511 falls back to COG; if COG is also "not available" the
        // heading must end up as 0, not 360.
        var msg = CreatePositionReportMessage(trueHeading: 511, cog: 360.0);
        var result = _mapper.Map(msg);

        result!.Position!.TrueHeading.Should().Be(0);
    }

    // --- Class B Tests ---

    [Fact]
    public void MapStandardClassB_MapsPositionWithNotDefinedStatus()
    {
        var json = JsonSerializer.Serialize(new
        {
            StandardClassBPositionReport = new
            {
                UserID = 265000099,
                Sog = 6.0,
                Cog = 120.0,
                TrueHeading = 511, // not available → falls back to COG
                Latitude = 57.6,
                Longitude = 11.8
            }
        });
        var msg = new AisMessage
        {
            MessageType = "StandardClassBPositionReport",
            Message = JsonDocument.Parse(json).RootElement,
            MetaData = new AisMetaData { Mmsi = 265000099, Latitude = 57.6, Longitude = 11.8 }
        };

        var result = _mapper.Map(msg);

        result.Should().NotBeNull();
        result!.MessageType.Should().Be("StandardClassBPositionReport");
        result.Mmsi.Should().Be(265000099);
        result.Position.Should().NotBeNull();
        result.Position!.Latitude.Should().Be(57.6);
        result.Position.SpeedOverGround.Should().Be(6.0);
        ((int)result.Position.Status).Should().Be(15); // NotDefined, not 0 ("under way")
        result.Position.TrueHeading.Should().Be(120.0); // COG fallback
    }

    [Fact]
    public void MapExtendedClassB_MapsPositionAndStaticData()
    {
        var json = JsonSerializer.Serialize(new
        {
            ExtendedClassBPositionReport = new
            {
                UserID = 265000098,
                Sog = 4.0,
                Cog = 90.0,
                TrueHeading = 90,
                Latitude = 57.5,
                Longitude = 11.7,
                Name = "SEA SCOUT@@@",
                Type = 37, // PleasureCraft
                Dimension = new { A = 5, B = 5, C = 2, D = 2 }
            }
        });
        var msg = new AisMessage
        {
            MessageType = "ExtendedClassBPositionReport",
            Message = JsonDocument.Parse(json).RootElement,
            MetaData = new AisMetaData { Mmsi = 265000098, Latitude = 57.5, Longitude = 11.7 }
        };

        var result = _mapper.Map(msg);

        result.Should().NotBeNull();
        result!.Position.Should().NotBeNull();
        result.StaticData.Should().NotBeNull();
        result.StaticData!.Name.Should().Be("SEA SCOUT");
        result.StaticData.ShipType.Should().Be(VesselType.PleasureCraft);
        result.StaticData.LengthOverall.Should().Be(10);
    }

    [Fact]
    public void MapStaticDataReport_MapsNameTypeAndCallSign()
    {
        var json = JsonSerializer.Serialize(new
        {
            StaticDataReport = new
            {
                UserID = 265000097,
                ReportA = new { Name = "LITTLE WING@@" },
                ReportB = new { ShipType = 30, CallSign = "SXYZ", Dimension = new { A = 3, B = 3, C = 1, D = 1 } }
            }
        });
        var msg = new AisMessage
        {
            MessageType = "StaticDataReport",
            Message = JsonDocument.Parse(json).RootElement,
            MetaData = new AisMetaData { Mmsi = 265000097 }
        };

        var result = _mapper.Map(msg);

        result.Should().NotBeNull();
        result!.StaticData.Should().NotBeNull();
        result.StaticData!.Name.Should().Be("LITTLE WING");
        result.StaticData.CallSign.Should().Be("SXYZ");
        result.StaticData.ShipType.Should().Be(VesselType.Fishing);
    }

    // --- Static Data Tests ---

    [Fact]
    public void MapStaticData_ValidData_MapsCorrectly()
    {
        var msg = CreateStaticDataMessage();
        var result = _mapper.Map(msg);

        result.Should().NotBeNull();
        result!.MessageType.Should().Be("ShipStaticData");
        result.Mmsi.Should().Be(265000001);
        result.StaticData.Should().NotBeNull();
        result.StaticData!.Name.Should().Be("STENA GERMANICA");
        result.StaticData.CallSign.Should().Be("SBHI");
        result.StaticData.ImoNumber.Should().Be(9145176);
        result.StaticData.Destination.Should().Be("SEGOT");
        result.StaticData.DimensionA.Should().Be(140);
        result.StaticData.DimensionB.Should().Be(48);
        result.StaticData.LengthOverall.Should().Be(188);
        result.StaticData.Beam.Should().Be(30);
    }

    [Fact]
    public void MapStaticData_DraughtConvertedFromDecimeters()
    {
        var msg = CreateStaticDataMessage(draught: 61);
        var result = _mapper.Map(msg);

        result!.StaticData!.Draught.Should().Be(6.1);
    }

    [Fact]
    public void MapStaticData_CleanAisString_TrimsAtSymbols()
    {
        var msg = CreateStaticDataMessage(name: "SHIP NAME@@@@@@");
        var result = _mapper.Map(msg);

        result!.StaticData!.Name.Should().Be("SHIP NAME");
    }

    [Fact]
    public void MapStaticData_CleanAisString_TrimsWhitespace()
    {
        var msg = CreateStaticDataMessage(name: "  SHIP NAME  ");
        var result = _mapper.Map(msg);

        result!.StaticData!.Name.Should().Be("SHIP NAME");
    }

    [Fact]
    public void MapStaticData_CleanAisString_AllAtSymbols_ReturnsNull()
    {
        var msg = CreateStaticDataMessage(name: "@@@@@@@@@@");
        var result = _mapper.Map(msg);

        result!.StaticData!.Name.Should().BeNull();
    }

    [Fact]
    public void MapStaticData_CleanAisString_Null_ReturnsNull()
    {
        var msg = CreateStaticDataMessage(name: null);
        var result = _mapper.Map(msg);

        result!.StaticData!.Name.Should().BeNull();
    }

    [Fact]
    public void MapStaticData_ValidEta_MapsCorrectly()
    {
        var msg = CreateStaticDataMessage(etaMonth: 6, etaDay: 15, etaHour: 14, etaMinute: 30);
        var result = _mapper.Map(msg);

        result!.StaticData!.ReportedEta.Should().NotBeNull();
        result.StaticData.ReportedEta!.Value.Month.Should().Be(6);
        result.StaticData.ReportedEta.Value.Day.Should().Be(15);
        result.StaticData.ReportedEta.Value.Hour.Should().Be(14);
        result.StaticData.ReportedEta.Value.Minute.Should().Be(30);
    }

    [Fact]
    public void MapStaticData_ZeroMonthEta_ReturnsNullEta()
    {
        var msg = CreateStaticDataMessage(etaMonth: 0, etaDay: 15);
        var result = _mapper.Map(msg);

        result!.StaticData!.ReportedEta.Should().BeNull();
    }

    [Fact]
    public void MapStaticData_ZeroDayEta_ReturnsNullEta()
    {
        var msg = CreateStaticDataMessage(etaMonth: 6, etaDay: 0);
        var result = _mapper.Map(msg);

        result!.StaticData!.ReportedEta.Should().BeNull();
    }

    [Fact]
    public void MapStaticData_InvalidMonthEta_ReturnsNullEta()
    {
        var msg = CreateStaticDataMessage(etaMonth: 13, etaDay: 15);
        var result = _mapper.Map(msg);

        result!.StaticData!.ReportedEta.Should().BeNull();
    }

    [Fact]
    public void MapStaticData_NullInnerReport_ReturnsNull()
    {
        var json = JsonSerializer.Serialize(new { ShipStaticData = (object?)null });
        var msg = new AisMessage
        {
            MessageType = "ShipStaticData",
            Message = JsonDocument.Parse(json).RootElement,
            MetaData = new AisMetaData { Mmsi = 123 }
        };

        _mapper.Map(msg).Should().BeNull();
    }

    // --- Ship Type Mapping ---

    [Theory]
    [InlineData(20, VesselType.WingInGround)]
    [InlineData(29, VesselType.WingInGround)]
    [InlineData(30, VesselType.Fishing)]
    [InlineData(31, VesselType.Towing)]
    [InlineData(33, VesselType.Dredging)]
    [InlineData(34, VesselType.Diving)]
    [InlineData(35, VesselType.Military)]
    [InlineData(36, VesselType.Sailing)]
    [InlineData(37, VesselType.PleasureCraft)]
    [InlineData(40, VesselType.HighSpeedCraft)]
    [InlineData(49, VesselType.HighSpeedCraft)]
    [InlineData(50, VesselType.Pilot)]
    [InlineData(51, VesselType.SearchAndRescue)]
    [InlineData(52, VesselType.Tug)]
    [InlineData(53, VesselType.PortTender)]
    [InlineData(58, VesselType.MedicalTransport)]
    [InlineData(60, VesselType.Passenger)]
    [InlineData(69, VesselType.Passenger)]
    [InlineData(70, VesselType.Cargo)]
    [InlineData(79, VesselType.Cargo)]
    [InlineData(80, VesselType.Tanker)]
    [InlineData(89, VesselType.Tanker)]
    [InlineData(90, VesselType.OtherType)]
    [InlineData(99, VesselType.OtherType)]
    [InlineData(0, VesselType.Unknown)]
    [InlineData(10, VesselType.Unknown)]
    public void MapShipType_ReturnsExpectedType(int aisType, VesselType expected)
    {
        var msg = CreateStaticDataMessage(type: aisType);
        var result = _mapper.Map(msg);
        result!.StaticData!.ShipType.Should().Be(expected);
    }

    // --- Country Code Mapping ---

    [Theory]
    [InlineData(265000001, "SE")]
    [InlineData(219000001, "DK")]
    [InlineData(257000001, "NO")]
    [InlineData(230000001, "FI")]
    [InlineData(211000001, "DE")]
    [InlineData(244000001, "NL")]
    [InlineData(226000001, "FR")]
    [InlineData(232000001, "GB")]
    [InlineData(311000001, "BS")]
    [InlineData(366000001, "US")]
    [InlineData(412000001, "CN")]
    [InlineData(431000001, "JP")]
    [InlineData(440000001, "KR")]
    [InlineData(538000001, "MH")]
    [InlineData(636000001, "LR")]
    [InlineData(477000001, "HK")]
    public void MmsiToCountryCode_KnownMid_ReturnsCorrectCode(int mmsi, string expected)
    {
        var msg = CreateStaticDataMessage(mmsi: mmsi);
        var result = _mapper.Map(msg);
        result!.StaticData!.CountryCode.Should().Be(expected);
    }

    [Fact]
    public void MmsiToCountryCode_UnknownMid_ReturnsNull()
    {
        var msg = CreateStaticDataMessage(mmsi: 999000001);
        var result = _mapper.Map(msg);
        result!.StaticData!.CountryCode.Should().BeNull();
    }

    // --- Unknown Message Type ---

    [Fact]
    public void Map_UnknownMessageType_ReturnsNull()
    {
        var msg = new AisMessage
        {
            MessageType = "SomeUnknownType",
            Message = JsonDocument.Parse("{}").RootElement
        };

        _mapper.Map(msg).Should().BeNull();
    }
}
