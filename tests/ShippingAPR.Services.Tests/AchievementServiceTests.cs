using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class AchievementServiceTests : IDisposable
{
    private readonly VesselStore _store = new();
    private readonly NotificationService _notificationService;
    private readonly AchievementService _sut;

    public AchievementServiceTests()
    {
        // Delete persisted data to prevent cross-test contamination
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShippingAPR", "achievements.json");
        if (File.Exists(dataPath)) File.Delete(dataPath);

        var areaMonitor = new AreaMonitorService(_store, NullLogger<AreaMonitorService>.Instance);
        _notificationService = new NotificationService(
            areaMonitor,
            NullLogger<NotificationService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new TrackingOptions()));
        _sut = new AchievementService(
            _store,
            _notificationService,
            NullLogger<AchievementService>.Instance);
    }

    [Fact]
    public void Definitions_ContainsExpectedAchievements()
    {
        _sut.Definitions.Should().NotBeEmpty();
        _sut.Definitions.Should().Contain(d => d.Id == "spot_10");
        _sut.Definitions.Should().Contain(d => d.Id == "types_3");
        _sut.Definitions.Should().Contain(d => d.Id == "flags_5");
        _sut.Definitions.Should().Contain(d => d.Id == "speed_20");
    }

    [Fact]
    public void SpottingVessels_IncrementsUniqueCount()
    {
        AddVessel(100000001, VesselType.Cargo, "SE");
        AddVessel(100000002, VesselType.Tanker, "NO");
        AddVessel(100000003, VesselType.Fishing, "DK");

        _sut.UniqueVesselsSpotted.Should().Be(3);
        _sut.TypesDiscovered.Should().Be(3);
        _sut.CountriesTracked.Should().Be(3);
    }

    [Fact]
    public void SameVessel_NotCountedTwice()
    {
        AddVessel(100000001, VesselType.Cargo, "SE");
        _store.AddOrUpdate(100000001, CreatePosition(58.0, 12.0, 10), null);

        _sut.UniqueVesselsSpotted.Should().Be(1);
    }

    [Fact]
    public void SpottingTenVessels_UnlocksDeckCadet()
    {
        for (int i = 100000001; i <= 100000010; i++)
            AddVessel(i, VesselType.Cargo, "SE");

        var progress = _sut.Progress;
        progress.Should().ContainKey("spot_10");
        progress["spot_10"].IsUnlocked.Should().BeTrue();
        _sut.TotalUnlocked.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void FastVessel_UnlocksSpeedAchievement()
    {
        _store.AddOrUpdate(100000001, CreatePosition(57.7, 11.9, 25), null);

        var progress = _sut.Progress;
        progress.Should().ContainKey("speed_20");
        progress["speed_20"].IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public void RareType_UnlocksRareCatch()
    {
        _store.AddOrUpdate(100000001, CreatePosition(57.7, 11.9),
            new VesselStaticData { ShipType = VesselType.Military, CountryCode = "US" });

        var progress = _sut.Progress;
        progress.Should().ContainKey("rare_1");
        progress["rare_1"].IsUnlocked.Should().BeTrue();
    }

    private void AddVessel(int mmsi, VesselType type, string country)
    {
        _store.AddOrUpdate(mmsi, CreatePosition(57.7 + (mmsi - 100000000) * 0.01, 11.9),
            new VesselStaticData { ShipType = type, CountryCode = country });
    }

    private static VesselPosition CreatePosition(double lat, double lon, double speed = 5.0)
    {
        return new VesselPosition
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = speed,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = NavigationalStatus.UnderWayUsingEngine
        };
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
