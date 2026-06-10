using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="ITrackHistoryStore"/>. Timestamps are stored as Unix
/// milliseconds (UTC) for cheap range queries. Writes are serialized with a lock;
/// reads use independent pooled connections.
/// </summary>
public sealed class SqliteTrackHistoryStore : ITrackHistoryStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _initialized;

    public SqliteTrackHistoryStore(IOptions<TrackHistoryOptions> options)
        : this(options.Value.DatabasePath) { }

    public SqliteTrackHistoryStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = true
        }.ToString();
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _initialized) == 1) return;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized == 1) return;

            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS track_points (
                    mmsi INTEGER NOT NULL,
                    lat  REAL    NOT NULL,
                    lon  REAL    NOT NULL,
                    sog  REAL    NOT NULL,
                    ts   INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_track_mmsi_ts ON track_points(mmsi, ts);
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task AddPointAsync(int mmsi, TrackPoint point, CancellationToken ct = default)
        => AddPointsAsync(mmsi, new[] { point }, ct);

    public async Task AddPointsAsync(int mmsi, IEnumerable<TrackPoint> points, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO track_points (mmsi, lat, lon, sog, ts) VALUES ($mmsi, $lat, $lon, $sog, $ts)";
            var pMmsi = cmd.Parameters.Add("$mmsi", SqliteType.Integer);
            var pLat = cmd.Parameters.Add("$lat", SqliteType.Real);
            var pLon = cmd.Parameters.Add("$lon", SqliteType.Real);
            var pSog = cmd.Parameters.Add("$sog", SqliteType.Real);
            var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);

            foreach (var p in points)
            {
                pMmsi.Value = mmsi;
                pLat.Value = p.Latitude;
                pLon.Value = p.Longitude;
                pSog.Value = p.SpeedOverGround;
                pTs.Value = ToUnixMs(p.Timestamp);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<TrackPoint>> GetTrackAsync(int mmsi, DateTime sinceUtc, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        var result = new List<TrackPoint>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT lat, lon, sog, ts FROM track_points WHERE mmsi = $mmsi AND ts >= $since ORDER BY ts";
        cmd.Parameters.AddWithValue("$mmsi", mmsi);
        cmd.Parameters.AddWithValue("$since", ToUnixMs(sinceUtc));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new TrackPoint(
                reader.GetDouble(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                FromUnixMs(reader.GetInt64(3))));
        }
        return result;
    }

    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM track_points WHERE ts < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", ToUnixMs(cutoffUtc));
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static long ToUnixMs(DateTime dt) =>
        // Convert Local kinds instead of silently relabeling them as UTC;
        // Unspecified is assumed UTC (the convention throughout this codebase).
        new DateTimeOffset(dt.Kind == DateTimeKind.Local
            ? dt.ToUniversalTime()
            : DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static DateTime FromUnixMs(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    public void Dispose() => _writeLock.Dispose();
}
