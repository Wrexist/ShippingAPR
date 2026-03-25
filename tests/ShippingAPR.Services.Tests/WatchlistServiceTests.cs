using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class WatchlistServiceTests : IDisposable
{
    private readonly VesselStore _store = new();
    private readonly NotificationService _notificationService;
    private readonly WatchlistService _sut;

    public WatchlistServiceTests()
    {
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShippingAPR", "watchlist.json");
        if (File.Exists(dataPath)) File.Delete(dataPath);

        var areaMonitor = new AreaMonitorService(_store, NullLogger<AreaMonitorService>.Instance);
        _notificationService = new NotificationService(
            areaMonitor,
            NullLogger<NotificationService>.Instance,
            Options.Create(new TrackingOptions()));
        _sut = new WatchlistService(
            _store,
            _notificationService,
            NullLogger<WatchlistService>.Instance);
    }

    [Fact]
    public void InitialWatchlist_IsEmpty()
    {
        _sut.WatchedMmsis.Should().BeEmpty();
    }

    [Fact]
    public void Add_AddsToWatchlist()
    {
        _sut.Add(200000001);

        _sut.IsWatched(200000001).Should().BeTrue();
        _sut.WatchedMmsis.Should().ContainSingle();
    }

    [Fact]
    public void AddDuplicate_IsIdempotent()
    {
        _sut.Add(200000001);
        _sut.Add(200000001);

        _sut.WatchedMmsis.Should().HaveCount(1);
    }

    [Fact]
    public void Remove_RemovesFromWatchlist()
    {
        _sut.Add(200000001);
        _sut.Remove(200000001);

        _sut.IsWatched(200000001).Should().BeFalse();
    }

    [Fact]
    public void Toggle_AddsAndRemoves()
    {
        _sut.Toggle(200000001);
        _sut.IsWatched(200000001).Should().BeTrue();

        _sut.Toggle(200000001);
        _sut.IsWatched(200000001).Should().BeFalse();
    }

    [Fact]
    public void WatchlistChanged_EventFired()
    {
        var changedCount = 0;
        _sut.WatchlistChanged += (_, _) => changedCount++;

        _sut.Add(200000001);
        _sut.Remove(200000001);

        changedCount.Should().Be(2);
    }

    [Fact]
    public void WatchedVesselAppearing_PublishesNotification()
    {
        _sut.Add(200000001);

        _store.AddOrUpdate(200000001, CreatePosition(), null);

        _notificationService.History.Should().Contain(n => n.Title.Contains("Watched Vessel"));
    }

    [Fact]
    public void UnwatchedVesselAppearing_NoNotification()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), null);

        _notificationService.History.Should().NotContain(n => n.Title.Contains("Watched Vessel"));
    }

    [Fact]
    public void WatchedVessel_NotifiedOnlyOnce()
    {
        _sut.Add(200000001);

        _store.AddOrUpdate(200000001, CreatePosition(), null);
        _store.AddOrUpdate(200000001, CreatePosition(), null);

        var watchNotifications = _notificationService.History
            .Count(n => n.Title.Contains("Watched Vessel"));
        watchNotifications.Should().Be(1);
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

    public void Dispose()
    {
        _sut.Dispose();
    }
}
