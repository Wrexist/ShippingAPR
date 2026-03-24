using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core.Models;

public sealed class Vessel
{
    public const int DefaultMaxTrackPoints = 200;

    private readonly int _maxTrackPoints;

    public int Mmsi { get; init; }
    public VesselPosition? CurrentPosition { get; set; }
    public VesselStaticData? StaticData { get; set; }
    public EtaResult? CalculatedEta { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    private readonly object _trackLock = new();
    private readonly TrackPoint[] _trackBuffer;
    private int _trackHead;
    private int _trackCount;

    public Vessel(int maxTrackPoints = DefaultMaxTrackPoints)
    {
        _maxTrackPoints = maxTrackPoints > 0 ? maxTrackPoints : DefaultMaxTrackPoints;
        _trackBuffer = new TrackPoint[_maxTrackPoints];
    }

    /// <summary>Returns a snapshot of the track history (thread-safe).</summary>
    public IReadOnlyList<TrackPoint> Track
    {
        get
        {
            lock (_trackLock)
            {
                if (_trackCount == 0) return [];

                var result = new TrackPoint[_trackCount];
                var start = (_trackHead - _trackCount + _maxTrackPoints) % _maxTrackPoints;
                for (int i = 0; i < _trackCount; i++)
                {
                    result[i] = _trackBuffer[(start + i) % _maxTrackPoints];
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

                _trackHead = (_trackHead + 1) % _maxTrackPoints;
                if (_trackCount < _maxTrackPoints)
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
