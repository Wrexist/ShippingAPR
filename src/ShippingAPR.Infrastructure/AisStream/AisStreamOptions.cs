namespace ShippingAPR.Infrastructure.AisStream;

public sealed class AisStreamOptions
{
    public const string SectionName = "AisStream";

    public string ApiKey { get; set; } = string.Empty;
    public string WebSocketUrl { get; set; } = "wss://stream.aisstream.io/v0/stream";
    public int ReconnectMaxDelaySeconds { get; set; } = 30;
    public int ReceiveBufferSize { get; set; } = 8192;
}
