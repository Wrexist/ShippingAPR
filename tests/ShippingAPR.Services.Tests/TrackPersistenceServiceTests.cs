using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.Persistence;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class TrackPersistenceServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shippingapr-persist-{Guid.NewGuid():N}.db");
    private readonly VesselStore _store = new();
    private readonly SqliteTrackHistoryStore _history;
    private readonly TrackPersistenceService _sut;

    public TrackPersistenceServiceTests()
    {
        _history = new SqliteTrackHistoryStore(_dbPath);
        _sut = new TrackPersistenceService(
            _store, _history,
            Options.Create(new TrackHistoryOptions { DatabasePath = _dbPath, RetentionDays = 7 }),
            NullLogger<TrackPersistenceService>.Instance);
    }

    [Fact]
    public async Task Flush_PersistsLatestPositionPerVessel()
    {
        _store.AddOrUpdate(265000001, Position(57.7, 11.9, 10), null);

        await _sut.FlushAsync(CancellationToken.None);

        var track = await _history.GetTrackAsync(265000001, DateTime.MinValue);
        track.Should().ContainSingle();
        track[0].Latitude.Should().Be(57.7);
    }

    [Fact]
    public async Task Flush_WithNoUpdates_PersistsNothing()
    {
        await _sut.FlushAsync(CancellationToken.None);
        (await _history.GetTrackAsync(265000001, DateTime.MinValue)).Should().BeEmpty();
    }

    private static VesselPosition Position(double lat, double lon, double sog) => new()
    {
        Latitude = lat, Longitude = lon,
        SpeedOverGround = sog, CourseOverGround = 0, TrueHeading = 0,
        Status = NavigationalStatus.UnderWayUsingEngine
    };

    public void Dispose()
    {
        _history.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
