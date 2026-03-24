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

    private readonly object _trackLock = new();
    private readonly TrackPoint[] _trackBuffer = new TrackPoint[MaxTrackPoints];
    private int _trackHead;
    private int _trackCount;

    /// <summary>Returns a snapshot of the track history (thread-safe).</summary>
    public IReadOnlyList<TrackPoint> Track
    {
        get
        {
            lock (_trackLock)
            {
                if (_trackCount == 0) return [];

                var result = new TrackPoint[_trackCount];
                var start = (_trackHead - _trackCount + MaxTrackPoints) % MaxTrackPoints;
                for (int i = 0; i < _trackCount; i++)
                {
                    result[i] = _trackBuffer[(start + i) % MaxTrackPoints];
                }
                return result;
            }
        }
    }

    public string DisplayName =>
        StaticData?.Name ?? $"MMSI {Mmsi}";

    public VesselType Type =>
        StaticData?.ShipType ?? VesselType.Unknown;

    public void UpdatePosition(VesselPosition position)
    {
        lock (_trackLock)
        {
            if (CurrentPosition is not null)
            {
                _trackBuffer[_trackHead] = new TrackPoint(
                    CurrentPosition.Latitude,
                    CurrentPosition.Longitude,
                    CurrentPosition.SpeedOverGround,
                    CurrentPosition.Timestamp);

                _trackHead = (_trackHead + 1) % MaxTrackPoints;
                if (_trackCount < MaxTrackPoints)
                    _trackCount++;
            }
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
