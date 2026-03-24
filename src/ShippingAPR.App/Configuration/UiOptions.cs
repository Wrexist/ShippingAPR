namespace ShippingAPR.App.Configuration;

public sealed class UiOptions
{
    public const string SectionName = "Ui";

    public int MapBatchUpdateMs { get; set; } = 250;
    public int ViewportDebounceMs { get; set; } = 500;
    public int VesselListRefreshMs { get; set; } = 3000;
    public int NotificationTimeoutMs { get; set; } = 5000;
    public int StartupDelayMs { get; set; } = 500;
    public double DefaultCenterLon { get; set; } = 10.0;
    public double DefaultCenterLat { get; set; } = 54.0;
    public double DefaultResolution { get; set; } = 2446;
    public double ClusterResolutionThreshold { get; set; } = 1000;
    public int ClusterGridCellPx { get; set; } = 80;
    public double AreaChangeThreshold { get; set; } = 0.1;
}
