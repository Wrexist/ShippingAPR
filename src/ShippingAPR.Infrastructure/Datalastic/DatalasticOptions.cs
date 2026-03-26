namespace ShippingAPR.Infrastructure.Datalastic;

public sealed class DatalasticOptions
{
    public const string SectionName = "Datalastic";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.datalastic.com/api/v0";
    public int PollIntervalSeconds { get; set; } = 15;
    public int RequestTimeoutSeconds { get; set; } = 10;
}
