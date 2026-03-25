namespace ShippingAPR.Infrastructure.Weather;

public sealed class MarineWeatherOptions
{
    public const string SectionName = "MarineWeather";

    public string BaseUrl { get; set; } = "https://marine-api.open-meteo.com";
    public string ForecastBaseUrl { get; set; } = "https://api.open-meteo.com";
    public int PollIntervalMinutes { get; set; } = 10;
    public int CacheTtlMinutes { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int GridSteps { get; set; } = 8;
}
