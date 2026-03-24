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
}
