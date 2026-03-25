namespace ShippingAPR.Core.Models;

public sealed class PortActivityRecord
{
    public required string PortName { get; init; }
    public required int Mmsi { get; init; }
    public required string VesselName { get; init; }
    public required PortActivityType ActivityType { get; init; }
    public required DateTime Timestamp { get; init; }
    public double? SpeedKnots { get; init; }
}

public enum PortActivityType
{
    Arrival,
    Departure,
    InPort
}

public sealed class PortCongestionSnapshot
{
    public required string PortName { get; init; }
    public int VesselsInPort { get; init; }
    public int ArrivalsLast24h { get; init; }
    public int DeparturesLast24h { get; init; }
    public double CongestionScore { get; init; } // 0.0 (empty) to 1.0 (congested)
    public List<PortActivityRecord> RecentActivity { get; init; } = [];
    public Dictionary<int, int> HourlyTraffic { get; init; } = new(); // hour -> count
}
