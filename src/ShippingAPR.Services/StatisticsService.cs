using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;

namespace ShippingAPR.Services;

public sealed class StatisticsSnapshot
{
    public int TotalVessels { get; init; }
    public Dictionary<VesselType, int> VesselCountByType { get; init; } = new();
    public double AverageSpeed { get; init; }
    public int VesselsUnderWay { get; init; }
    public int VesselsAtAnchor { get; init; }
    public int VesselsMoored { get; init; }
    public List<(string Destination, int Count)> TopDestinations { get; init; } = [];
    public int WatchedVesselCount { get; init; }
}

public sealed class StatisticsService
{
    private readonly IVesselStore _vesselStore;
    private readonly IWatchlistService _watchlistService;

    public StatisticsService(IVesselStore vesselStore, IWatchlistService watchlistService)
    {
        _vesselStore = vesselStore;
        _watchlistService = watchlistService;
    }

    public StatisticsSnapshot GetSnapshot()
    {
        var vessels = _vesselStore.Vessels.Values.ToList();
        var withPosition = vessels.Where(v => v.CurrentPosition is not null).ToList();

        var countByType = withPosition
            .GroupBy(v => v.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var avgSpeed = withPosition.Count > 0
            ? withPosition.Average(v => v.CurrentPosition!.SpeedOverGround)
            : 0;

        var underWay = withPosition.Count(v =>
            v.CurrentPosition!.Status == NavigationalStatus.UnderWayUsingEngine ||
            v.CurrentPosition.Status == NavigationalStatus.UnderWaySailing);

        var atAnchor = withPosition.Count(v =>
            v.CurrentPosition!.Status == NavigationalStatus.AtAnchor);

        var moored = withPosition.Count(v =>
            v.CurrentPosition!.Status == NavigationalStatus.Moored);

        var topDest = vessels
            .Where(v => !string.IsNullOrEmpty(v.StaticData?.Destination))
            .GroupBy(v => v.StaticData!.Destination!)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => (g.Key, g.Count()))
            .ToList();

        var watchedCount = withPosition.Count(v => _watchlistService.IsWatched(v.Mmsi));

        return new StatisticsSnapshot
        {
            TotalVessels = vessels.Count,
            VesselCountByType = countByType,
            AverageSpeed = avgSpeed,
            VesselsUnderWay = underWay,
            VesselsAtAnchor = atAnchor,
            VesselsMoored = moored,
            TopDestinations = topDest,
            WatchedVesselCount = watchedCount
        };
    }
}
