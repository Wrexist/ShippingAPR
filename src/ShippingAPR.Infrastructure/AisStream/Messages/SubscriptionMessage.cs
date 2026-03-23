using System.Text.Json.Serialization;

namespace ShippingAPR.Infrastructure.AisStream.Messages;

public sealed class SubscriptionMessage
{
    [JsonPropertyName("APIKey")]
    public required string ApiKey { get; init; }

    [JsonPropertyName("BoundingBoxes")]
    public required double[][][] BoundingBoxes { get; init; }

    [JsonPropertyName("FilterMessageTypes")]
    public string[]? FilterMessageTypes { get; init; }

    [JsonPropertyName("FiltersShipMMSI")]
    public string[]? FiltersShipMmsi { get; init; }
}
