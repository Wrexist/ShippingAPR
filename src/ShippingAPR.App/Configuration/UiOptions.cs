namespace ShippingAPR.App.Configuration;

public sealed class UiOptions
{
    public const string SectionName = "Ui";

    // Timing
    public int MapBatchUpdateMs { get; set; } = 250;
    public int ViewportDebounceMs { get; set; } = 500;
    public int VesselListRefreshMs { get; set; } = 3000;
    public int NotificationTimeoutMs { get; set; } = 5000;
    public int StartupDelayMs { get; set; } = 500;

    // Map defaults
    public double DefaultCenterLon { get; set; } = 10.0;
    public double DefaultCenterLat { get; set; } = 54.0;
    public double DefaultResolution { get; set; } = 2446;

    // Clustering
    public double ClusterResolutionThreshold { get; set; } = 1000;
    public int ClusterGridCellPx { get; set; } = 80;
    public double AreaChangeThreshold { get; set; } = 0.1;

    // Vessel symbol scaling
    public double VesselScaleNormal { get; set; } = 0.4;
    public double VesselScaleHighlighted { get; set; } = 0.6;

    // Cluster symbol scaling
    public double ClusterScaleMin { get; set; } = 0.3;
    public double ClusterScalePerVessel { get; set; } = 0.05;
    public double ClusterScaleMax { get; set; } = 1.0;

    // Zoom resolution levels for region navigation
    public double ZoomGlobalResolution { get; set; } = 9784;
    public double ZoomContinentalResolution { get; set; } = 4892;
    public double ZoomRegionalResolution { get; set; } = 2446;

    // Connection status colors (hex ARGB)
    public string StatusColorConnected { get; set; } = "#FF00D68F";
    public string StatusColorConnecting { get; set; } = "#FFFFAA00";
    public string StatusColorError { get; set; } = "#FFFF3D71";

    // Filter defaults
    public double DefaultMaxSpeedFilter { get; set; } = 50;

    // Search
    public int SearchMaxResults { get; set; } = 20;
}
