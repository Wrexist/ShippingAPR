using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class FleetServiceTests
{
    private readonly VesselStore _store = new();
    private readonly FleetService _sut;

    public FleetServiceTests()
    {
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShippingAPR", "fleets.json");
        if (File.Exists(dataPath)) File.Delete(dataPath);

        _sut = new FleetService(_store, NullLogger<FleetService>.Instance);
    }

    [Fact]
    public void InitialGroups_IsEmpty()
    {
        _sut.Groups.Should().BeEmpty();
    }

    [Fact]
    public void AddGroup_AppearsInList()
    {
        var group = new FleetGroup { Name = "My Fleet", MmsiList = [100000001, 100000002] };
        _sut.AddGroup(group);

        _sut.Groups.Should().ContainSingle(g => g.Name == "My Fleet");
    }

    [Fact]
    public void RemoveGroup_RemovesFromList()
    {
        _sut.AddGroup(new FleetGroup { Name = "Remove Me" });
        _sut.RemoveGroup("Remove Me");

        _sut.Groups.Should().BeEmpty();
    }

    [Fact]
    public void GroupsChanged_EventFired()
    {
        var changedCount = 0;
        _sut.GroupsChanged += (_, _) => changedCount++;

        _sut.AddGroup(new FleetGroup { Name = "Test" });
        _sut.RemoveGroup("Test");

        changedCount.Should().Be(2);
    }

    [Fact]
    public void AutoGroupByFlag_GroupsByCountryCode()
    {
        AddVessel(200000001, "SE", VesselType.Cargo);
        AddVessel(200000002, "SE", VesselType.Tanker);
        AddVessel(200000003, "NO", VesselType.Cargo);

        var groups = _sut.GetAutoGroupsByFlag();

        groups.Should().HaveCount(2);
        groups.Should().Contain(g => g.Name == "Flag: SE" && g.MmsiList.Count == 2);
        groups.Should().Contain(g => g.Name == "Flag: NO" && g.MmsiList.Count == 1);
    }

    [Fact]
    public void AutoGroupByType_GroupsByVesselType()
    {
        AddVessel(200000001, "SE", VesselType.Cargo);
        AddVessel(200000002, "NO", VesselType.Cargo);
        AddVessel(200000003, "DK", VesselType.Tanker);

        var groups = _sut.GetAutoGroupsByType();

        groups.Should().HaveCount(2);
        groups.Should().Contain(g => g.MmsiList.Count == 2);
    }

    [Fact]
    public void AutoGroupByFlag_SkipsVesselsWithoutCountryCode()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), new VesselStaticData { CountryCode = "" });
        _store.AddOrUpdate(200000002, CreatePosition(), null);

        var groups = _sut.GetAutoGroupsByFlag();

        groups.Should().BeEmpty();
    }

    private void AddVessel(int mmsi, string country, VesselType type)
    {
        _store.AddOrUpdate(mmsi, CreatePosition(),
            new VesselStaticData { ShipType = type, CountryCode = country });
    }

    private static VesselPosition CreatePosition()
    {
        return new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
            SpeedOverGround = 10,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = NavigationalStatus.UnderWayUsingEngine
        };
    }
}
