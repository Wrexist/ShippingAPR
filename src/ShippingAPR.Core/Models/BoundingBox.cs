namespace ShippingAPR.Core.Models;

public sealed record BoundingBox(
    double MinLatitude,
    double MinLongitude,
    double MaxLatitude,
    double MaxLongitude)
{
    public bool Contains(double latitude, double longitude) =>
        latitude >= MinLatitude && latitude <= MaxLatitude &&
        longitude >= MinLongitude && longitude <= MaxLongitude;

    /// <summary>Gothenburg harbor default.</summary>
    public static BoundingBox GothenburgDefault =>
        new(57.65, 11.80, 57.75, 12.05);

    public double[][] ToAisStreamFormat() =>
    [
        [MinLatitude, MinLongitude],
        [MaxLatitude, MaxLongitude]
    ];
}
