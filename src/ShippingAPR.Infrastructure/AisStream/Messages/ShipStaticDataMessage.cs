using System.Text.Json.Serialization;

namespace ShippingAPR.Infrastructure.AisStream.Messages;

public sealed class ShipStaticDataMessage
{
    [JsonPropertyName("ShipStaticData")]
    public ShipStaticDataPayload? ShipStaticData { get; set; }
}

public sealed class ShipStaticDataPayload
{
    [JsonPropertyName("UserID")]
    public int UserId { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("CallSign")]
    public string? CallSign { get; set; }

    [JsonPropertyName("ImoNumber")]
    public int ImoNumber { get; set; }

    [JsonPropertyName("Destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("Type")]
    public int Type { get; set; }

    [JsonPropertyName("MaximumStaticDraught")]
    public double MaximumStaticDraught { get; set; }

    [JsonPropertyName("Dimension")]
    public ShipDimension? Dimension { get; set; }

    [JsonPropertyName("Eta")]
    public ShipEta? Eta { get; set; }
}

public sealed class ShipDimension
{
    [JsonPropertyName("A")]
    public int A { get; set; }

    [JsonPropertyName("B")]
    public int B { get; set; }

    [JsonPropertyName("C")]
    public int C { get; set; }

    [JsonPropertyName("D")]
    public int D { get; set; }
}

public sealed class ShipEta
{
    [JsonPropertyName("Month")]
    public int Month { get; set; }

    [JsonPropertyName("Day")]
    public int Day { get; set; }

    [JsonPropertyName("Hour")]
    public int Hour { get; set; }

    [JsonPropertyName("Minute")]
    public int Minute { get; set; }
}
