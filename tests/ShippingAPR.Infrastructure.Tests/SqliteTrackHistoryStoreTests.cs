using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.Persistence;
using Xunit;

namespace ShippingAPR.Infrastructure.Tests;

public class SqliteTrackHistoryStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shippingapr-track-{Guid.NewGuid():N}.db");
    private readonly SqliteTrackHistoryStore _store;
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public SqliteTrackHistoryStoreTests()
    {
        _store = new SqliteTrackHistoryStore(_dbPath);
    }

    [Fact]
    public async Task AddAndGet_RoundTripsPointsInOrder()
    {
        await _store.AddPointAsync(265000001, new TrackPoint(57.7, 11.9, 12.5, T0));
        await _store.AddPointAsync(265000001, new TrackPoint(57.8, 12.0, 13.0, T0.AddMinutes(1)));

        var track = await _store.GetTrackAsync(265000001, T0.AddDays(-1));

        track.Should().HaveCount(2);
        track[0].Latitude.Should().Be(57.7);
        track[0].Timestamp.Should().Be(T0);
        track[1].SpeedOverGround.Should().Be(13.0);
        track.Should().BeInAscendingOrder(p => p.Timestamp);
    }

    [Fact]
    public async Task AddPoints_BatchInsert_Persists()
    {
        await _store.AddPointsAsync(900000001, new[]
        {
            new TrackPoint(1, 1, 1, T0),
            new TrackPoint(2, 2, 2, T0.AddMinutes(5)),
            new TrackPoint(3, 3, 3, T0.AddMinutes(10)),
        });

        (await _store.GetTrackAsync(900000001, DateTime.MinValue)).Should().HaveCount(3);
    }

    [Fact]
    public async Task GetTrack_FiltersBySinceAndMmsi()
    {
        await _store.AddPointAsync(1001, new TrackPoint(1, 1, 1, T0));
        await _store.AddPointAsync(1001, new TrackPoint(2, 2, 2, T0.AddHours(2)));
        await _store.AddPointAsync(2002, new TrackPoint(3, 3, 3, T0.AddHours(2)));

        var track = await _store.GetTrackAsync(1001, T0.AddHours(1));

        track.Should().HaveCount(1);
        track[0].Latitude.Should().Be(2);
    }

    [Fact]
    public async Task Purge_RemovesPointsOlderThanCutoff()
    {
        await _store.AddPointAsync(1, new TrackPoint(1, 1, 1, T0));
        await _store.AddPointAsync(1, new TrackPoint(2, 2, 2, T0.AddDays(10)));

        var removed = await _store.PurgeOlderThanAsync(T0.AddDays(5));

        removed.Should().Be(1);
        var track = await _store.GetTrackAsync(1, DateTime.MinValue);
        track.Should().ContainSingle().Which.Latitude.Should().Be(2);
    }

    public void Dispose()
    {
        _store.Dispose();
        // Release the pooled file handle so the temp DB can be deleted.
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
