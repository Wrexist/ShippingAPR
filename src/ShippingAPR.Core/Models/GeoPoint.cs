namespace ShippingAPR.Core.Models;

/// <summary>A geographic point in WGS84 degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude);
