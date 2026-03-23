using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

public sealed class VesselStaticData
{
    public string? Name { get; init; }
    public string? CallSign { get; init; }
    public int ImoNumber { get; init; }
    public VesselType ShipType { get; init; }
    public string? Destination { get; init; }
    public DateTime? ReportedEta { get; init; }
    public double Draught { get; init; }
    public int DimensionA { get; init; }
    public int DimensionB { get; init; }
    public int DimensionC { get; init; }
    public int DimensionD { get; init; }
    public int LengthOverall => DimensionA + DimensionB;
    public int Beam => DimensionC + DimensionD;
    public string? CountryCode { get; init; }
}
