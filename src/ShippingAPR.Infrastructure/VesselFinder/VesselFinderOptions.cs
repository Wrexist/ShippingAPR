namespace ShippingAPR.Infrastructure.VesselFinder;

public sealed class VesselFinderOptions
{
    public const string SectionName = "VesselFinder";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.vesselfinder.com";
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 500;
}
