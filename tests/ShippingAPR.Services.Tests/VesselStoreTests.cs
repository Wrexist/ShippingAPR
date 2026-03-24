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
        _store.AddOrUpdate(1, CreatePosition(57.7, 11.9), null);
        _store.AddOrUpdate(2, CreatePosition(57.8, 12.0), null);

        var cleared = false;
        _store.StoreCleared += (_, _) => cleared = true;

        _store.Clear();

        _store.Count.Should().Be(0);
        cleared.Should().BeTrue();
    }

    [Fact]
    public void PurgeStale_RemovesOldVessels()
    {
        var vessel = _store.AddOrUpdate(1, CreatePosition(57.7, 11.9), null);
        // Simulate old update
        vessel.LastUpdated = DateTime.UtcNow.AddMinutes(-15);

        _store.AddOrUpdate(2, CreatePosition(57.8, 12.0), null); // Fresh

        var purged = _store.PurgeStale(TimeSpan.FromMinutes(10));

        purged.Should().Be(1);
        _store.Count.Should().Be(1);
        _store.GetByMmsi(1).Should().BeNull();
        _store.GetByMmsi(2).Should().NotBeNull();
    }

    [Fact]
    public void TrackHistory_CappedAtMax()
    {
        // Add many position updates to test track capping
        for (int i = 0; i < Vessel.DefaultMaxTrackPoints + 50; i++)
        {
            _store.AddOrUpdate(1, CreatePosition(57.7 + i * 0.001, 11.9), null);
        }

        var vessel = _store.GetByMmsi(1);
        vessel.Should().NotBeNull();
        vessel!.Track.Count.Should().BeLessOrEqualTo(Vessel.DefaultMaxTrackPoints);
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
