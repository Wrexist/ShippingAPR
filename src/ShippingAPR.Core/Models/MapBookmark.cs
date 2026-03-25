namespace ShippingAPR.Core.Models;

/// <summary>
/// A saved map location for quick navigation.
/// </summary>
public sealed record MapBookmark
{
    public required string Name { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double ZoomLevel { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
