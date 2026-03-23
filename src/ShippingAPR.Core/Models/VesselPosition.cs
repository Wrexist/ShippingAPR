using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

public sealed class VesselPosition
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double SpeedOverGround { get; init; }
    public double CourseOverGround { get; init; }
    public double TrueHeading { get; init; }
    public NavigationalStatus Status { get; init; }
    public double RateOfTurn { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
