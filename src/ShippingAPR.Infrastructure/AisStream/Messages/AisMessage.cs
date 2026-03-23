using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShippingAPR.Infrastructure.AisStream.Messages;

public sealed class AisMessage
{
    [JsonPropertyName("MessageType")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("Message")]
    public JsonElement Message { get; set; }

    [JsonPropertyName("MetaData")]
    public AisMetaData? MetaData { get; set; }
}

public sealed class AisMetaData
{
    [JsonPropertyName("MMSI")]
    public int Mmsi { get; set; }

    [JsonPropertyName("MMSI_String")]
    public string? MmsiString { get; set; }

    [JsonPropertyName("ShipName")]
    public string? ShipName { get; set; }

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("time_utc")]
    public string? TimeUtc { get; set; }
}
