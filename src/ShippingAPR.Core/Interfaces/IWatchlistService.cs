namespace ShippingAPR.Core.Interfaces;

public interface IWatchlistService
{
    IReadOnlyCollection<int> WatchedMmsis { get; }
    bool IsWatched(int mmsi);
    void Add(int mmsi);
    void Remove(int mmsi);
    void Toggle(int mmsi);
    event EventHandler<int>? WatchlistChanged;
}
