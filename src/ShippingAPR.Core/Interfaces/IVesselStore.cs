using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

public interface IVesselStore
{
    IReadOnlyDictionary<int, Vessel> Vessels { get; }
    int Count { get; }
    Vessel AddOrUpdate(int mmsi, VesselPosition? position, VesselStaticData? staticData);
    Vessel? GetByMmsi(int mmsi);
    IEnumerable<Vessel> Search(string query);
    int PurgeStale(TimeSpan maxAge);
    void Clear();
    event EventHandler<Vessel>? VesselUpdated;
    event EventHandler<Vessel>? VesselAdded;
    event EventHandler<Vessel>? VesselRemoved;
    event EventHandler? StoreCleared;
}
