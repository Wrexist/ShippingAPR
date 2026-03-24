using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class VesselStore : IVesselStore
{
    private readonly ConcurrentDictionary<int, Vessel> _vessels = new();
    private readonly int _maxTrackPoints;
    private SynchronizationContext? _syncContext;

    public IReadOnlyDictionary<int, Vessel> Vessels => _vessels;
    public int Count => _vessels.Count;

    public event EventHandler<Vessel>? VesselUpdated;
    public event EventHandler<Vessel>? VesselAdded;
    public event EventHandler<Vessel>? VesselRemoved;
    public event EventHandler? StoreCleared;

    public VesselStore() : this(Vessel.DefaultMaxTrackPoints) { }

    public VesselStore(IOptions<TrackingOptions> trackingOptions)
        : this(trackingOptions.Value.MaxTrackPoints) { }

    private VesselStore(int maxTrackPoints)
    {
        _maxTrackPoints = maxTrackPoints;
    }

    public void SetSynchronizationContext(SynchronizationContext? context) =>
        _syncContext = context;

    public Vessel AddOrUpdate(int mmsi, VesselPosition? position, VesselStaticData? staticData)
    {
        var isNew = false;

        var vessel = _vessels.AddOrUpdate(
            mmsi,
            _ =>
            {
                isNew = true;
                var v = new Vessel(_maxTrackPoints) { Mmsi = mmsi };
                if (position is not null) v.UpdatePosition(position);
                if (staticData is not null) v.UpdateStaticData(staticData);
                return v;
            },
            (_, existing) =>
            {
                if (position is not null) existing.UpdatePosition(position);
                if (staticData is not null) existing.UpdateStaticData(staticData);
                return existing;
            });

        if (isNew)
            RaiseOnContext(VesselAdded, vessel);
        else
            RaiseOnContext(VesselUpdated, vessel);

        return vessel;
    }

    public Vessel? GetByMmsi(int mmsi) =>
        _vessels.TryGetValue(mmsi, out var vessel) ? vessel : null;

    public IEnumerable<Vessel> Search(string query)
    {
        // Snapshot values to avoid concurrent modification during enumeration
        if (string.IsNullOrWhiteSpace(query))
            return _vessels.Values.ToList();

        var q = query.Trim();

        // Try MMSI search
        if (int.TryParse(q, out var mmsi))
        {
            return _vessels.Values.Where(v =>
                v.Mmsi.ToString().Contains(q) ||
                (v.StaticData?.ImoNumber.ToString().Contains(q) ?? false))
                .ToList();
        }

        return _vessels.Values.Where(v =>
            (v.StaticData?.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (v.StaticData?.CallSign?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (v.StaticData?.Destination?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    public void Clear()
    {
        _vessels.Clear();
        RaiseOnContext(StoreCleared);
    }

    /// <summary>
    /// Remove vessels that haven't been updated within the given timespan.
    /// </summary>
    public int PurgeStale(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var staleKeys = _vessels
            .Where(kv => kv.Value.LastUpdated < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            if (_vessels.TryRemove(key, out var removed))
                RaiseOnContext(VesselRemoved, removed);
        }

        return staleKeys.Count;
    }

    private void RaiseOnContext(EventHandler<Vessel>? handler, Vessel vessel)
    {
        if (handler is null) return;

        if (_syncContext is not null)
            _syncContext.Post(_ => handler.Invoke(this, vessel), null);
        else
            handler.Invoke(this, vessel);
    }

    private void RaiseOnContext(EventHandler? handler)
    {
        if (handler is null) return;

        if (_syncContext is not null)
            _syncContext.Post(_ => handler.Invoke(this, EventArgs.Empty), null);
        else
            handler.Invoke(this, EventArgs.Empty);
    }
}
