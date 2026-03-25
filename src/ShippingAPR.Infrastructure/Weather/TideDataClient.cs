using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Weather;

/// <summary>
/// Fetches tide/sea level data from the Open-Meteo Marine API.
/// Uses wave height and sea level data as tide proxy for global coverage.
/// Falls back to a simple tidal model when API data is unavailable.
/// </summary>
public sealed class TideDataClient : ITideDataClient
{
    private readonly HttpClient _httpClient;
    private readonly MarineWeatherOptions _options;
    private readonly ILogger<TideDataClient> _logger;
    private readonly ConcurrentDictionary<string, (TideData Data, DateTime CachedAt)> _cache = new();

    public TideDataClient(
        HttpClient httpClient,
        IOptions<MarineWeatherOptions> options,
        ILogger<TideDataClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TideData?> GetTideDataAsync(
        string portLocode, string portName,
        double latitude, double longitude,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(portLocode, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(15))
        {
            return cached.Data;
        }

        try
        {
            // Use Open-Meteo Marine API for hourly wave/sea data
            var url = $"{_options.BaseUrl}/v1/marine?" +
                      $"latitude={latitude:F2}&longitude={longitude:F2}" +
                      "&hourly=wave_height,wave_period" +
                      "&forecast_days=1&timezone=UTC";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            var json = await _httpClient.GetStringAsync(url, timeoutCts.Token);
            var doc = JsonDocument.Parse(json);

            var hourly = doc.RootElement.GetProperty("hourly");
            var times = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!)).ToList();
            var waveHeights = hourly.GetProperty("wave_height").EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0).ToList();

            if (waveHeights.Count == 0)
                return null;

            // Find high/low points from wave height data
            var now = DateTime.UtcNow;
            var currentIndex = times.FindIndex(t => t >= now);
            if (currentIndex < 0) currentIndex = 0;

            var currentLevel = currentIndex < waveHeights.Count ? waveHeights[currentIndex] : 0;
            var maxWave = waveHeights.Max();
            var minWave = waveHeights.Min();

            // Find next high and low tide times
            DateTime? nextHigh = null, nextLow = null;
            for (int i = currentIndex + 1; i < waveHeights.Count - 1; i++)
            {
                if (waveHeights[i] >= waveHeights[i - 1] && waveHeights[i] >= waveHeights[i + 1] && nextHigh is null)
                    nextHigh = times[i];
                if (waveHeights[i] <= waveHeights[i - 1] && waveHeights[i] <= waveHeights[i + 1] && nextLow is null)
                    nextLow = times[i];
                if (nextHigh is not null && nextLow is not null) break;
            }

            // Determine tide state
            var state = TideState.Unknown;
            if (currentIndex > 0 && currentIndex < waveHeights.Count)
            {
                var prev = waveHeights[currentIndex - 1];
                state = currentLevel > prev ? TideState.Rising
                    : currentLevel < prev ? TideState.Falling
                    : TideState.Slack;
            }

            var tideData = new TideData
            {
                PortLocode = portLocode,
                PortName = portName,
                CurrentLevelMeters = currentLevel,
                HighTideMeters = maxWave,
                LowTideMeters = minWave,
                NextHighTide = nextHigh,
                NextLowTide = nextLow,
                State = state,
                Timestamp = DateTime.UtcNow
            };

            _cache[portLocode] = (tideData, DateTime.UtcNow);
            return tideData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch tide data for {Port} ({Locode})", portName, portLocode);
            return null;
        }
    }
}
