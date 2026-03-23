namespace ShippingAPR.Core.Models;

public sealed record Port(
    string Locode,
    string Name,
    string Country,
    double Latitude,
    double Longitude);
