using FluentAssertions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class ExportServiceTests
{
    private readonly VesselStore _store = new();
    private readonly ExportService _exportService;

    public ExportServiceTests()
    {
        _exportService = new ExportService(_store);
    }

    [Fact]
    public void ExportToCsv_EmptyStore_ReturnsHeaderOnly()
    {
        var csv = _exportService.ExportToCsv();

        csv.Should().StartWith("Name,MMSI,Type,");
        csv.Trim().Split('\n').Should().HaveCount(1);
    }

    [Fact]
    public void ExportToCsv_WithVessels_IncludesData()
    {
        _store.AddOrUpdate(265000001,
            new VesselPosition
            {
                Latitude = 57.70,
                Longitude = 11.97,
                SpeedOverGround = 12.5,
                CourseOverGround = 45.0,
                TrueHeading = 47.0,
                Timestamp = DateTime.UtcNow
            },
            new VesselStaticData
            {
                Name = "TEST VESSEL",
                ShipType = VesselType.Cargo,
                Destination = "SEGOT",
                CountryCode = "SE"
            });

        var csv = _exportService.ExportToCsv();
        var lines = csv.Trim().Split('\n');

        lines.Should().HaveCount(2);
        lines[1].Should().Contain("TEST VESSEL");
        lines[1].Should().Contain("265000001");
        lines[1].Should().Contain("Cargo");
        lines[1].Should().Contain("SE");
    }

    [Fact]
    public void ExportToJson_EmptyStore_ReturnsEmptyArray()
    {
        var json = _exportService.ExportToJson();

        json.Should().Contain("[]");
    }

    [Fact]
    public void ExportToJson_WithVessels_ReturnsValidJson()
    {
        _store.AddOrUpdate(265000001,
            new VesselPosition
            {
                Latitude = 57.70,
                Longitude = 11.97,
                SpeedOverGround = 12.5,
                CourseOverGround = 45.0,
                TrueHeading = 47.0,
                Timestamp = DateTime.UtcNow
            },
            new VesselStaticData
            {
                Name = "TEST VESSEL",
                ShipType = VesselType.Cargo
            });

        var json = _exportService.ExportToJson();

        json.Should().Contain("\"mmsi\": 265000001");
        json.Should().Contain("\"name\": \"TEST VESSEL\"");
    }

    [Fact]
    public void ExportToCsv_VesselWithCommaInName_EscapesCorrectly()
    {
        _store.AddOrUpdate(265000002, null,
            new VesselStaticData
            {
                Name = "SHIP, THE GREAT",
                ShipType = VesselType.Tanker
            });

        var csv = _exportService.ExportToCsv();

        csv.Should().Contain("\"SHIP, THE GREAT\"");
    }

    [Fact]
    public void ExportTrackToGpx_WithTrackPoints_ReturnsValidGpx()
    {
        var vessel = CreateVesselWithTrack();

        var gpx = _exportService.ExportTrackToGpx(vessel);

        gpx.Should().Contain("<?xml version=\"1.0\"");
        gpx.Should().Contain("<gpx");
        gpx.Should().Contain("<trk>");
        gpx.Should().Contain("<trkpt");
        gpx.Should().Contain("lat=\"57.700000\"");
        gpx.Should().Contain("lon=\"11.970000\"");
        gpx.Should().Contain("<speed>");
        gpx.Should().Contain("</gpx>");
    }

    [Fact]
    public void ExportTrackToGpx_EmptyTrack_ReturnsValidGpxWithNoPoints()
    {
        var vessel = new Vessel { Mmsi = 265000001 };

        var gpx = _exportService.ExportTrackToGpx(vessel);

        gpx.Should().Contain("<trkseg>");
        gpx.Should().Contain("</trkseg>");
        gpx.Should().NotContain("<trkpt");
    }

    [Fact]
    public void ExportTrackToKml_WithTrackPoints_ReturnsValidKml()
    {
        var vessel = CreateVesselWithTrack();

        var kml = _exportService.ExportTrackToKml(vessel);

        kml.Should().Contain("<?xml version=\"1.0\"");
        kml.Should().Contain("<kml");
        kml.Should().Contain("<LineString>");
        kml.Should().Contain("<coordinates>");
        kml.Should().Contain("11.970000,57.700000,0");
        kml.Should().Contain("</kml>");
    }

    [Fact]
    public void ExportTrackToGpx_XmlSpecialChars_AreEscaped()
    {
        var vessel = new Vessel { Mmsi = 265000001 };
        vessel.UpdateStaticData(new VesselStaticData
        {
            Name = "SHIP <TEST> & \"QUOTED\"",
            ShipType = VesselType.Cargo
        });

        var gpx = _exportService.ExportTrackToGpx(vessel);

        gpx.Should().Contain("&amp;");
        gpx.Should().Contain("&lt;");
        gpx.Should().Contain("&gt;");
        gpx.Should().NotContain("<TEST>");
    }

    private static Vessel CreateVesselWithTrack()
    {
        var vessel = new Vessel { Mmsi = 265000001 };
        vessel.UpdateStaticData(new VesselStaticData
        {
            Name = "TEST VESSEL",
            ShipType = VesselType.Cargo
        });

        // Add some track points
        vessel.UpdatePosition(new VesselPosition
        {
            Latitude = 57.70, Longitude = 11.97,
            SpeedOverGround = 10.0, CourseOverGround = 45.0,
            TrueHeading = 45.0, Timestamp = DateTime.UtcNow.AddMinutes(-10)
        });
        vessel.UpdatePosition(new VesselPosition
        {
            Latitude = 57.72, Longitude = 12.00,
            SpeedOverGround = 11.0, CourseOverGround = 50.0,
            TrueHeading = 50.0, Timestamp = DateTime.UtcNow.AddMinutes(-5)
        });
        vessel.UpdatePosition(new VesselPosition
        {
            Latitude = 57.75, Longitude = 12.05,
            SpeedOverGround = 12.0, CourseOverGround = 55.0,
            TrueHeading = 55.0, Timestamp = DateTime.UtcNow
        });

        return vessel;
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+2")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void Escape_FormulaInjection_PrefixedWithQuote(string input)
    {
        // None of these contain a comma/quote/newline, so the only change is the
        // neutralising leading single quote.
        ExportService.Escape(input).Should().Be("'" + input);
    }

    [Fact]
    public void Escape_PlainText_Unchanged()
    {
        ExportService.Escape("STENA GERMANICA").Should().Be("STENA GERMANICA");
    }

    [Fact]
    public void Escape_CarriageReturn_IsQuoted()
    {
        ExportService.Escape("LINE1\rLINE2").Should().StartWith("\"").And.EndWith("\"");
    }
}
