using System.Text.Json.Serialization;

namespace ShippingAPR.Infrastructure.AisStream.Messages;

// Class B transceivers (pleasure craft, fishing boats, tugs, most small vessels)
// broadcast different message types from Class A. Without these, a large share of
// coastal/harbour traffic is invisible.

public sealed class StandardClassBPositionReportMessage
{
    [JsonPropertyName("StandardClassBPositionReport")]
    public ClassBPositionReportData? StandardClassBPositionReport { get; set; }
}

public sealed class ExtendedClassBPositionReportMessage
{
    [JsonPropertyName("ExtendedClassBPositionReport")]
    public ExtendedClassBPositionReportData? ExtendedClassBPositionReport { get; set; }
}

public sealed class ClassBPositionReportData
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
}

public sealed class ExtendedClassBPositionReportData
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

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Type")]
    public int Type { get; set; }

    [JsonPropertyName("Dimension")]
    public ShipDimension? Dimension { get; set; }
}

public sealed class StaticDataReportMessage
{
    [JsonPropertyName("StaticDataReport")]
    public StaticDataReportData? StaticDataReport { get; set; }
}

public sealed class StaticDataReportData
{
    [JsonPropertyName("UserID")]
    public int UserId { get; set; }

    [JsonPropertyName("ReportA")]
    public StaticDataReportA? ReportA { get; set; }

    [JsonPropertyName("ReportB")]
    public StaticDataReportB? ReportB { get; set; }
}

public sealed class StaticDataReportA
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
}

public sealed class StaticDataReportB
{
    [JsonPropertyName("ShipType")]
    public int ShipType { get; set; }

    [JsonPropertyName("CallSign")]
    public string? CallSign { get; set; }

    [JsonPropertyName("Dimension")]
    public ShipDimension? Dimension { get; set; }
}
