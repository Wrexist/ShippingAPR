using FluentAssertions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class VesselStoreTests
{
    private readonly VesselStore _store = new();

    [Fact]
    public void AddOrUpdate_NewVessel_AddsAndRaisesEvent()
    {
        Vessel? addedVessel = null;
        _store.VesselAdded += (_, v) => addedVessel = v;

        var position = CreatePosition(57.7, 11.9);
        var vessel = _store.AddOrUpdate(123456789, position, null);

        vessel.Mmsi.Should().Be(123456789);
        vessel.CurrentPosition.Should().Be(position);
        _store.Count.Should().Be(1);
        addedVessel.Should().NotBeNull();
    }

    [Fact]
    public void AddOrUpdate_ExistingVessel_UpdatesAndRaisesEvent()
    {
        var pos1 = CreatePosition(57.7, 11.9);
        _store.AddOrUpdate(123456789, pos1, null);

        Vessel? updatedVessel = null;
        _store.VesselUpdated += (_, v) => updatedVessel = v;

        var pos2 = CreatePosition(57.8, 12.0);
        var vessel = _store.AddOrUpdate(123456789, pos2, null);

        vessel.CurrentPosition.Should().Be(pos2);
        vessel.Track.Should().HaveCount(1);
        _store.Count.Should().Be(1);
        updatedVessel.Should().NotBeNull();
    }

    [Fact]
    public void AddOrUpdate_WithStaticData_MergesData()
    {
        var position = CreatePosition(57.7, 11.9);
        _store.AddOrUpdate(123456789, position, null);

        var staticData = new VesselStaticData
        {
            Name = "Test Ship",
            ImoNumber = 9876543,
            ShipType = VesselType.Cargo
        };

        var vessel = _store.AddOrUpdate(123456789, null, staticData);

        vessel.StaticData.Should().NotBeNull();
        vessel.StaticData!.Name.Should().Be("Test Ship");
        vessel.CurrentPosition.Should().Be(position); // Position preserved
    }

    [Fact]
    public void GetByMmsi_ExistingVessel_ReturnsVessel()
    {
        _store.AddOrUpdate(123456789, CreatePosition(57.7, 11.9), null);

        var vessel = _store.GetByMmsi(123456789);
        vessel.Should().NotBeNull();
        vessel!.Mmsi.Should().Be(123456789);
    }

    [Fact]
    public void GetByMmsi_NonExistent_ReturnsNull()
    {
        _store.GetByMmsi(999999999).Should().BeNull();
    }

    [Fact]
    public void Search_ByName_FindsVessel()
    {
        var staticData = new VesselStaticData { Name = "STENA GERMANICA" };
        _store.AddOrUpdate(265000001, CreatePosition(57.7, 11.9), staticData);

        var results = _store.Search("STENA").ToList();
        results.Should().HaveCount(1);
        results[0].StaticData!.Name.Should().Be("STENA GERMANICA");
    }

    [Fact]
    public void Search_ByMmsi_FindsVessel()
    {
        _store.AddOrUpdate(265000001, CreatePosition(57.7, 11.9), null);

        var results = _store.Search("265000001").ToList();
        results.Should().HaveCount(1);
    }

    [Fact]
    public void Clear_RemovesAllVessels()
    {
        _store.AddOrUpdate(100000001, CreatePosition(57.7, 11.9), null);
        _store.AddOrUpdate(100000002, CreatePosition(57.8, 12.0), null);

        var cleared = false;
        _store.StoreCleared += (_, _) => cleared = true;

        _store.Clear();

        _store.Count.Should().Be(0);
        cleared.Should().BeTrue();
    }

    [Fact]
    public void PurgeStale_RemovesOldVessels()
    {
        var vessel = _store.AddOrUpdate(100000001, CreatePosition(57.7, 11.9), null);
        // Simulate old update
        vessel.LastUpdated = DateTime.UtcNow.AddMinutes(-15);

        _store.AddOrUpdate(100000002, CreatePosition(57.8, 12.0), null); // Fresh

        var purged = _store.PurgeStale(TimeSpan.FromMinutes(10));

        purged.Should().Be(1);
        _store.Count.Should().Be(1);
        _store.GetByMmsi(100000001).Should().BeNull();
        _store.GetByMmsi(100000002).Should().NotBeNull();
    }

    [Fact]
    public void TrackHistory_CappedAtMax()
    {
        // Add many position updates to test track capping
        for (int i = 0; i < Vessel.DefaultMaxTrackPoints + 50; i++)
        {
            _store.AddOrUpdate(100000001, CreatePosition(57.7 + i * 0.001, 11.9), null);
        }

        var vessel = _store.GetByMmsi(100000001);
        vessel.Should().NotBeNull();
        vessel!.Track.Count.Should().BeLessOrEqualTo(Vessel.DefaultMaxTrackPoints);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99_999_999)]
    [InlineData(800_000_000)]
    [InlineData(-1)]
    public void AddOrUpdate_InvalidMmsi_ThrowsArgumentOutOfRange(int invalidMmsi)
    {
        var act = () => _store.AddOrUpdate(invalidMmsi, CreatePosition(57.7, 11.9), null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(100_000_000)]
    [InlineData(265000001)]
    [InlineData(799_999_999)]
    public void AddOrUpdate_ValidMmsi_Succeeds(int validMmsi)
    {
        var vessel = _store.AddOrUpdate(validMmsi, CreatePosition(57.7, 11.9), null);
        vessel.Should().NotBeNull();
        vessel.Mmsi.Should().Be(validMmsi);
    }

    [Fact]
    public void Search_RespectsMaxResults()
    {
        for (int i = 0; i < 30; i++)
        {
            _store.AddOrUpdate(100000000 + i, CreatePosition(57.7, 11.9),
                new VesselStaticData { Name = $"Ship {i}" });
        }

        var results = _store.Search("Ship", maxResults: 5).ToList();
        results.Should().HaveCountLessOrEqualTo(5);
    }

    [Fact]
    public void Search_WithWhitespace_ReturnsAllVessels()
    {
        _store.AddOrUpdate(265000001, CreatePosition(57.7, 11.9), null);

        var results = _store.Search("   ").ToList();
        results.Should().HaveCount(1); // Whitespace-only returns all
    }

    [Fact]
    public void Search_EmptyString_ReturnsAllVessels()
    {
        _store.AddOrUpdate(265000001, CreatePosition(57.7, 11.9), null);

        var results = _store.Search("").ToList();
        results.Should().HaveCount(1);
    }

    [Fact]
    public void Search_MmsiWithWhitespace_FindsVessel()
    {
        _store.AddOrUpdate(265000001, CreatePosition(57.7, 11.9), null);

        var results = _store.Search("  265000001  ").ToList();
        results.Should().HaveCount(1);
    }

    [Fact]
    public void Search_ByCallSign_FindsVessel()
    {
        var staticData = new VesselStaticData { CallSign = "SFDG" };
        _store.AddOrUpdate(265000001, CreatePosition(57.7, 11.9), staticData);

        var results = _store.Search("SFDG").ToList();
        results.Should().HaveCount(1);
    }

    [Fact]
    public void Search_ByDestination_FindsVessel()
    {
        var staticData = new VesselStaticData { Destination = "GOTHENBURG" };
        _store.AddOrUpdate(265000001, CreatePosition(57.7, 11.9), staticData);

        var results = _store.Search("GOTHEN").ToList();
        results.Should().HaveCount(1);
    }

    private static VesselPosition CreatePosition(double lat, double lon) =>
        new()
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = 10.0,
            CourseOverGround = 0,
            TrueHeading = 0,
            Status = NavigationalStatus.UnderWayUsingEngine,
            Timestamp = DateTime.UtcNow
        };
}
