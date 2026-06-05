using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Weather;

/// <summary>
/// Fetches marine weather data from the free Open-Meteo Marine API.
/// Includes wind, waves, sea temperature via a combined marine + forecast call.
/// </summary>
public sealed class OpenMeteoMarineClient : IMarineWeatherClient
{
    private readonly HttpClient _httpClient;
    private readonly MarineWeatherOptions _options;
    private readonly ILogger<OpenMeteoMarineClient> _logger;
    private readonly ConcurrentDictionary<string, (MarineWeather Data, DateTime CachedAt)> _cache = new();

    public OpenMeteoMarineClient(
        HttpClient httpClient,
        IOptions<MarineWeatherOptions> options,
        ILogger<OpenMeteoMarineClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MarineWeather?> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var cacheKey = $"{latitude:F1},{longitude:F1}";
        if (_cache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(_options.CacheTtlMinutes))
        {
            return cached.Data;
        }

        try
        {
            // Open-Meteo Marine API for wave data
            var marineUrl = $"{_options.BaseUrl}/v1/marine?" +
                            $"latitude={latitude:F2}&longitude={longitude:F2}" +
                            "&current=wave_height,wave_period,wave_direction" +
                            "&timezone=UTC";

            // Open-Meteo Forecast API for wind + temperature
            var forecastUrl = $"{_options.ForecastBaseUrl}/v1/forecast?" +
                              $"latitude={latitude:F2}&longitude={longitude:F2}" +
                              "&current=wind_speed_10m,wind_direction_10m,temperature_2m,visibility" +
                              "&wind_speed_unit=kn&timezone=UTC";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            // Fetch both in parallel
            var marineTask = _httpClient.GetStringAsync(marineUrl, timeoutCts.Token);
            var forecastTask = _httpClient.GetStringAsync(forecastUrl, timeoutCts.Token);

            await Task.WhenAll(marineTask, forecastTask);

            var marineJson = JsonDocument.Parse(await marineTask);
            var forecastJson = JsonDocument.Parse(await forecastTask);

            var marineCurrent = marineJson.RootElement.GetProperty("current");
            var forecastCurrent = forecastJson.RootElement.GetProperty("current");

            var windSpeed = GetDouble(forecastCurrent, "wind_speed_10m");
            var windDir = GetDouble(forecastCurrent, "wind_direction_10m");
            var waveHeight = GetDouble(marineCurrent, "wave_height");
            var wavePeriod = GetDouble(marineCurrent, "wave_period");
            var temp = GetDouble(forecastCurrent, "temperature_2m");
            // A missing visibility field must NOT read as 0 km (dense fog) and trigger a
            // false fog alert — default to a clear value when the model has no data.
            var visibility = GetDouble(forecastCurrent, "visibility", 10000.0) / 1000.0; // m to km

            var weather = new MarineWeather
            {
                Latitude = latitude,
                Longitude = longitude,
                WindSpeedKnots = windSpeed,
                WindDirectionDegrees = windDir,
                WaveHeightMeters = waveHeight,
                WavePeriodSeconds = wavePeriod,
                SeaTemperatureCelsius = temp,
                VisibilityKm = visibility,
                BeaufortScale = WindSpeedToBeaufort(windSpeed),
                Timestamp = DateTime.UtcNow
            };

            _cache[cacheKey] = (weather, DateTime.UtcNow);
            return weather;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch marine weather for {Lat},{Lon}", latitude, longitude);
            return null;
        }
    }

    public async Task<MarineWeatherGrid?> GetWeatherGridAsync(
        double minLat, double maxLat, double minLon, double maxLon,
        int steps = 8, CancellationToken ct = default)
    {
        var latStep = (maxLat - minLat) / Math.Max(steps - 1, 1);
        var lonStep = (maxLon - minLon) / Math.Max(steps - 1, 1);

        var tasks = new List<Task<MarineWeather?>>();
        for (int y = 0; y < steps; y++)
        {
            for (int x = 0; x < steps; x++)
            {
                var lat = minLat + y * latStep;
                var lon = minLon + x * lonStep;
                tasks.Add(GetWeatherAsync(lat, lon, ct));
            }
        }

        try
        {
            var results = await Task.WhenAll(tasks);
            var points = results.Where(r => r is not null).Cast<MarineWeather>().ToArray();

            if (points.Length == 0) return null;

            return new MarineWeatherGrid
            {
                Points = points,
                MinLat = minLat,
                MaxLat = maxLat,
                MinLon = minLon,
                MaxLon = maxLon,
                LatSteps = steps,
                LonSteps = steps
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch weather grid");
            return null;
        }
    }

    private static double GetDouble(JsonElement element, string property) =>
        GetDouble(element, property, 0);

    private static double GetDouble(JsonElement element, string property, double defaultValue)
    {
        if (!element.TryGetProperty(property, out var val)) return defaultValue;
        if (val.ValueKind == JsonValueKind.Number) return val.GetDouble();
        if (val.ValueKind == JsonValueKind.Null) return defaultValue;
        return double.TryParse(val.GetString(), out var d) ? d : defaultValue;
    }

    /// <summary>
    /// Converts wind speed in knots to the Beaufort scale (0-12).
    /// </summary>
    public static int WindSpeedToBeaufort(double knots) => knots switch
    {
        < 1 => 0,
        < 4 => 1,
        < 7 => 2,
        < 11 => 3,
        < 17 => 4,
        < 22 => 5,
        < 28 => 6,
        < 34 => 7,
        < 41 => 8,
        < 48 => 9,
        < 56 => 10,
        < 64 => 11,
        _ => 12
    };
}
