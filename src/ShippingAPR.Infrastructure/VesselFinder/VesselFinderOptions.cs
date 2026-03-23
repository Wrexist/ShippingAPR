namespace ShippingAPR.Infrastructure.VesselFinder;

public sealed class VesselFinderOptions
{
    public const string SectionName = "VesselFinder";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.vesselfinder.com";
}
