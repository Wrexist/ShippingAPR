namespace ShippingAPR.Services;

public sealed class TrackingOptions
{
    public const string SectionName = "Tracking";

    public int PurgeIntervalMinutes { get; set; } = 2;
    public int StaleAgeMinutes { get; set; } = 10;
    public double EtaDistanceThresholdNm { get; set; } = 0.5;
    public double EtaHeadingThresholdDeg { get; set; } = 5.0;
    public int PortCacheMaxSize { get; set; } = 500;
    public int MaxNotificationHistory { get; set; } = 100;
    public int MaxTrackPoints { get; set; } = 200;
}
