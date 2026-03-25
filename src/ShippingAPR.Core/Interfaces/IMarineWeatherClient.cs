using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

public interface IMarineWeatherClient
{
    Task<MarineWeather?> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default);
    Task<MarineWeatherGrid?> GetWeatherGridAsync(double minLat, double maxLat, double minLon, double maxLon, int steps = 8, CancellationToken ct = default);
}
