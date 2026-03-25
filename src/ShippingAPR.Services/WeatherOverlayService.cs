using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Manages weather data polling for the map overlay.
/// Fetches a grid of weather data for the current viewport.
/// </summary>
public sealed class WeatherOverlayService : IDisposable
{
    private readonly IMarineWeatherClient _weatherClient;
    private readonly ILogger<WeatherOverlayService> _logger;
    private MarineWeatherGrid? _currentGrid;

    public bool IsEnabled { get; set; }
    public MarineWeatherGrid? CurrentGrid => _currentGrid;

    public event EventHandler? WeatherUpdated;

    public WeatherOverlayService(
        IMarineWeatherClient weatherClient,
        ILogger<WeatherOverlayService> logger)
    {
        _weatherClient = weatherClient;
        _logger = logger;
    }

    public async Task RefreshAsync(double minLat, double maxLat, double minLon, double maxLon, CancellationToken ct = default)
    {
        if (!IsEnabled) return;

        try
        {
            var grid = await _weatherClient.GetWeatherGridAsync(minLat, maxLat, minLon, maxLon, 6, ct);
            if (grid is not null)
            {
                _currentGrid = grid;
                WeatherUpdated?.Invoke(this, EventArgs.Empty);
                _logger.LogDebug("Weather grid updated with {Count} points", grid.Points.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh weather overlay");
        }
    }

    public void Clear()
    {
        _currentGrid = null;
        WeatherUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}
