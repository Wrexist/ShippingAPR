namespace ShippingAPR.Core.Models;

public sealed record EtaResult(
    double DistanceNauticalMiles,
    TimeSpan TimeToArrival,
    DateTime EstimatedArrival,
    double CourseDeviationDegrees,
    double EffectiveSpeedKnots);
