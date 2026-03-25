namespace ShippingAPR.Core.Models;

/// <summary>
/// Real-time marine weather data for a specific coordinate.
/// </summary>
public sealed record MarineWeather
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double WindSpeedKnots { get; init; }
    public double WindDirectionDegrees { get; init; }
    public double WaveHeightMeters { get; init; }
    public double WavePeriodSeconds { get; init; }
    public double SeaTemperatureCelsius { get; init; }
    public double VisibilityKm { get; init; }
    public int BeaufortScale { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Weather data for a grid of points, used for map overlay rendering.
/// </summary>
public sealed class MarineWeatherGrid
{
    public required MarineWeather[] Points { get; init; }
    public required double MinLat { get; init; }
    public required double MaxLat { get; init; }
    public required double MinLon { get; init; }
    public required double MaxLon { get; init; }
    public required int LatSteps { get; init; }
    public required int LonSteps { get; init; }
    public DateTime FetchedAt { get; init; } = DateTime.UtcNow;
}
