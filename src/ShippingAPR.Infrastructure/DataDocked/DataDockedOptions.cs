namespace ShippingAPR.Infrastructure.DataDocked;

public sealed class DataDockedOptions
{
    public const string SectionName = "DataDocked";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.datadocked.com/v3";
    public int PollIntervalSeconds { get; set; } = 15;
    public int RequestTimeoutSeconds { get; set; } = 10;
}
