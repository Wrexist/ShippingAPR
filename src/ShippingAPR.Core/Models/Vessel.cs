using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

public sealed class Vessel
{
    public const int MaxTrackPoints = 200;

    public int Mmsi { get; init; }
    public VesselPosition? CurrentPosition { get; set; }
    public VesselStaticData? StaticData { get; set; }
    public EtaResult? CalculatedEta { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    private readonly List<TrackPoint> _track = new();
    public IReadOnlyList<TrackPoint> Track => _track;

    public string DisplayName =>
        StaticData?.Name ?? $"MMSI {Mmsi}";

    public VesselType Type =>
        StaticData?.ShipType ?? VesselType.Unknown;

    public void UpdatePosition(VesselPosition position)
    {
        if (CurrentPosition is not null)
        {
            _track.Add(new TrackPoint(
                CurrentPosition.Latitude,
                CurrentPosition.Longitude,
                CurrentPosition.SpeedOverGround,
                CurrentPosition.Timestamp));

            while (_track.Count > MaxTrackPoints)
                _track.RemoveAt(0);
        }

        CurrentPosition = position;
        LastUpdated = DateTime.UtcNow;
    }

    public void UpdateStaticData(VesselStaticData data)
    {
        StaticData = data;
        LastUpdated = DateTime.UtcNow;
    }
}
