namespace ShippingAPR.Core.Models;

public sealed record CpaResult(
    int Mmsi1,
    int Mmsi2,
    double CpaNauticalMiles,
    TimeSpan TimeToCpa);
