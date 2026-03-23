using System.Text.Json.Serialization;

namespace ShippingAPR.Infrastructure.AisStream.Messages;

public sealed class PositionReportMessage
{
    [JsonPropertyName("PositionReport")]
    public PositionReportData? PositionReport { get; set; }
}

public sealed class PositionReportData
{
    [JsonPropertyName("UserID")]
    public int UserId { get; set; }

    [JsonPropertyName("Sog")]
    public double Sog { get; set; }

    [JsonPropertyName("Cog")]
    public double Cog { get; set; }

    [JsonPropertyName("TrueHeading")]
    public int TrueHeading { get; set; }

    [JsonPropertyName("Latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("Longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("NavigationalStatus")]
    public int NavigationalStatus { get; set; }

    [JsonPropertyName("RateOfTurn")]
    public double RateOfTurn { get; set; }

    [JsonPropertyName("Timestamp")]
    public int Timestamp { get; set; }

    [JsonPropertyName("PositionAccuracy")]
    public bool PositionAccuracy { get; set; }

    [JsonPropertyName("Raim")]
    public bool Raim { get; set; }
}
