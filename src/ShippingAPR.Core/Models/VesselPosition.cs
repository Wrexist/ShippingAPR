using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

public sealed class VesselPosition
{
    private double _latitude;
    private double _longitude;

    public required double Latitude
    {
        get => _latitude;
        init
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Latitude must be a finite number", nameof(Latitude));
            _latitude = value is >= -90 and <= 90
                ? value
                : throw new ArgumentOutOfRangeException(nameof(Latitude), value, "Must be between -90 and 90");
        }
    }

    public required double Longitude
    {
        get => _longitude;
        init
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Longitude must be a finite number", nameof(Longitude));
            _longitude = value is >= -180 and <= 180
                ? value
                : throw new ArgumentOutOfRangeException(nameof(Longitude), value, "Must be between -180 and 180");
        }
    }

    public double SpeedOverGround { get; init; }
    public double CourseOverGround { get; init; }
    public double TrueHeading { get; init; }
    public NavigationalStatus Status { get; init; }
    public double RateOfTurn { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
