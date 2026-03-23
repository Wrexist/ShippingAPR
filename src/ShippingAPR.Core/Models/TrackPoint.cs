namespace ShippingAPR.Core.Models;

public sealed record TrackPoint(
    double Latitude,
    double Longitude,
    double SpeedOverGround,
    DateTime Timestamp);
