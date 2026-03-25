using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class HeatmapService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly ILogger<HeatmapService> _logger;
    private readonly object _lock = new();

    // Lightweight accumulator: lat/lon quantized to grid cells
    private readonly ConcurrentDictionary<(int latBucket, int lonBucket), int> _densityMap = new();
    private const double BucketSizeDegrees = 0.1; // ~11km at equator
    private int _totalPoints;

    public bool IsEnabled { get; set; }

    public event EventHandler? HeatmapUpdated;

    public HeatmapService(
        IVesselStore vesselStore,
        ILogger<HeatmapService> logger)
    {
        _vesselStore = vesselStore;
        _logger = logger;

        _vesselStore.VesselAdded += OnVesselUpdate;
        _vesselStore.VesselUpdated += OnVesselUpdate;
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        if (!IsEnabled) return;
        if (vessel.CurrentPosition is not { } pos) return;

        var latBucket = (int)(pos.Latitude / BucketSizeDegrees);
        var lonBucket = (int)(pos.Longitude / BucketSizeDegrees);

        _densityMap.AddOrUpdate(
            (latBucket, lonBucket),
            1,
            (_, count) => count + 1);

        Interlocked.Increment(ref _totalPoints);

        // Notify UI periodically (every 100 points to avoid spam)
        if (_totalPoints % 100 == 0)
            HeatmapUpdated?.Invoke(this, EventArgs.Empty);
    }

    public HeatmapGrid GenerateGrid(double minLat, double maxLat, double minLon, double maxLon, int resolution = 100)
    {
        var cells = new HeatmapCell[resolution, resolution];
        var latStep = (maxLat - minLat) / resolution;
        var lonStep = (maxLon - minLon) / resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                cells[y, x] = new HeatmapCell
                {
                    GridX = x,
                    GridY = y,
                    CenterLat = minLat + (y + 0.5) * latStep,
                    CenterLon = minLon + (x + 0.5) * lonStep
                };
            }
        }

        var grid = new HeatmapGrid
        {
            MinLat = minLat,
            MaxLat = maxLat,
            MinLon = minLon,
            MaxLon = maxLon,
            Resolution = resolution,
            Cells = cells
        };

        // Project accumulated density data onto the grid
        foreach (var kvp in _densityMap)
        {
            var lat = kvp.Key.latBucket * BucketSizeDegrees;
            var lon = kvp.Key.lonBucket * BucketSizeDegrees;
            grid.AddPoint(lat, lon);
        }

        // Apply simple gaussian-like smoothing
        ApplySmoothing(grid);

        return grid;
    }

    private static void ApplySmoothing(HeatmapGrid grid)
    {
        var res = grid.Resolution;
        var smoothed = new int[res, res];

        for (int y = 1; y < res - 1; y++)
        {
            for (int x = 1; x < res - 1; x++)
            {
                var sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        sum += grid.Cells[y + dy, x + dx].Intensity;

                smoothed[y, x] = sum / 5; // Light smoothing
            }
        }

        int max = 0;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                if (smoothed[y, x] > 0)
                    grid.Cells[y, x].Intensity = smoothed[y, x];
                if (grid.Cells[y, x].Intensity > max)
                    max = grid.Cells[y, x].Intensity;
            }
        }
        grid.MaxIntensity = max;
    }

    public void Clear()
    {
        _densityMap.Clear();
        _totalPoints = 0;
        _logger.LogDebug("Heatmap data cleared");
    }

    public int TotalDataPoints => _totalPoints;

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselUpdate;
        _vesselStore.VesselUpdated -= OnVesselUpdate;
    }
}
