using FluentAssertions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class VesselTests
{
    private static VesselPosition MakePosition(double lat = 57.7, double lon = 11.9, double sog = 10) =>
        new() { Latitude = lat, Longitude = lon, SpeedOverGround = sog, CourseOverGround = 45 };

    [Fact]
    public void NewVessel_HasNoPositionOrStaticData()
    {
        var vessel = new Vessel { Mmsi = 123456789 };

        vessel.CurrentPosition.Should().BeNull();
        vessel.StaticData.Should().BeNull();
        vessel.Track.Should().BeEmpty();
    }

    [Fact]
    public void UpdatePosition_SetsCurrentPositionAndLastUpdated()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        var before = DateTime.UtcNow;

        vessel.UpdatePosition(MakePosition());

        vessel.CurrentPosition.Should().NotBeNull();
        vessel.CurrentPosition!.Latitude.Should().Be(57.7);
        vessel.LastUpdated.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void UpdatePosition_FirstCall_DoesNotAddToTrack()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdatePosition(MakePosition());

        vessel.Track.Should().BeEmpty();
    }

    [Fact]
    public void UpdatePosition_SecondCall_AddsPreviousPositionToTrack()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdatePosition(MakePosition(57.7, 11.9));
        vessel.UpdatePosition(MakePosition(57.8, 12.0));

        vessel.Track.Should().HaveCount(1);
        vessel.Track[0].Latitude.Should().Be(57.7);
        vessel.Track[0].Longitude.Should().Be(11.9);
    }

    [Fact]
    public void UpdatePosition_MultipleUpdates_BuildsOrderedTrack()
    {
        var vessel = new Vessel { Mmsi = 123456789 };

        vessel.UpdatePosition(MakePosition(57.0, 11.0));
        vessel.UpdatePosition(MakePosition(57.1, 11.1));
        vessel.UpdatePosition(MakePosition(57.2, 11.2));
        vessel.UpdatePosition(MakePosition(57.3, 11.3));

        vessel.Track.Should().HaveCount(3);
        vessel.Track[0].Latitude.Should().Be(57.0);
        vessel.Track[1].Latitude.Should().Be(57.1);
        vessel.Track[2].Latitude.Should().Be(57.2);
    }

    [Fact]
    public void Track_CircularBufferWraps_WhenFull()
    {
        var vessel = new Vessel(maxTrackPoints: 3) { Mmsi = 123456789 };

        // Need 4 updates to fill buffer of 3 (first doesn't add to track)
        vessel.UpdatePosition(MakePosition(57.0, 11.0));
        vessel.UpdatePosition(MakePosition(57.1, 11.1));
        vessel.UpdatePosition(MakePosition(57.2, 11.2));
        vessel.UpdatePosition(MakePosition(57.3, 11.3));
        // Track should have 3 points: 57.0, 57.1, 57.2 (oldest 3)
        vessel.Track.Should().HaveCount(3);

        // One more update wraps the buffer
        vessel.UpdatePosition(MakePosition(57.4, 11.4));
        // Now track should have 57.1, 57.2, 57.3 (57.0 was overwritten)
        vessel.Track.Should().HaveCount(3);
        vessel.Track[0].Latitude.Should().Be(57.1);
        vessel.Track[2].Latitude.Should().Be(57.3);
    }

    [Fact]
    public void Track_ReturnsSnapshot_NotLiveReference()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdatePosition(MakePosition(57.0, 11.0));
        vessel.UpdatePosition(MakePosition(57.1, 11.1));

        var snapshot1 = vessel.Track;
        vessel.UpdatePosition(MakePosition(57.2, 11.2));
        var snapshot2 = vessel.Track;

        snapshot1.Should().HaveCount(1);
        snapshot2.Should().HaveCount(2);
    }

    [Fact]
    public void UpdateStaticData_SetsDataAndUpdatesTimestamp()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        var before = DateTime.UtcNow;

        vessel.UpdateStaticData(new VesselStaticData
        {
            Name = "TEST SHIP",
            ShipType = VesselType.Cargo
        });

        vessel.StaticData.Should().NotBeNull();
        vessel.StaticData!.Name.Should().Be("TEST SHIP");
        vessel.LastUpdated.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void DisplayName_WithName_ReturnsName()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData { Name = "STENA GERMANICA" });

        vessel.DisplayName.Should().Be("STENA GERMANICA");
    }

    [Fact]
    public void DisplayName_WithoutName_ReturnsMmsiFallback()
    {
        var vessel = new Vessel { Mmsi = 265000001 };
        vessel.DisplayName.Should().Be("MMSI 265000001");
    }

    [Fact]
    public void Type_WithStaticData_ReturnsShipType()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData { ShipType = VesselType.Tanker });

        vessel.Type.Should().Be(VesselType.Tanker);
    }

    [Fact]
    public void Type_WithoutStaticData_ReturnsUnknown()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.Type.Should().Be(VesselType.Unknown);
    }

    [Fact]
    public void Constructor_InvalidMaxTrackPoints_UsesDefault()
    {
        var vessel = new Vessel(maxTrackPoints: -1) { Mmsi = 1 };

        // Fill past default to verify it uses DefaultMaxTrackPoints
        for (int i = 0; i <= Vessel.DefaultMaxTrackPoints + 5; i++)
        {
            vessel.UpdatePosition(MakePosition(57.0 + i * 0.001, 11.0));
        }

        vessel.Track.Should().HaveCount(Vessel.DefaultMaxTrackPoints);
    }
}
