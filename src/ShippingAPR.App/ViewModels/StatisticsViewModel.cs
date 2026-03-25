using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using ShippingAPR.App.Configuration;
using ShippingAPR.Core.Enums;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class StatisticsViewModel : ObservableObject, IDisposable
{
    private readonly StatisticsService _statisticsService;
    private readonly SpotlightService _spotlightService;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty]
    private int _totalVessels;

    [ObservableProperty]
    private int _cargoCount;

    [ObservableProperty]
    private int _tankerCount;

    [ObservableProperty]
    private int _passengerCount;

    [ObservableProperty]
    private int _fishingCount;

    [ObservableProperty]
    private int _otherCount;

    [ObservableProperty]
    private string _averageSpeedText = "0.0 kn";

    [ObservableProperty]
    private int _underWayCount;

    [ObservableProperty]
    private int _atAnchorCount;

    [ObservableProperty]
    private int _mooredCount;

    [ObservableProperty]
    private int _watchedCount;

    [ObservableProperty]
    private string _topDestination1 = "";

    [ObservableProperty]
    private string _topDestination2 = "";

    [ObservableProperty]
    private string _topDestination3 = "";

    [ObservableProperty]
    private string _topDestination4 = "";

    [ObservableProperty]
    private string _topDestination5 = "";

    // Vessel of the Day Spotlight
    [ObservableProperty]
    private string _spotlightVesselName = "";

    [ObservableProperty]
    private string _spotlightReason = "";

    [ObservableProperty]
    private bool _hasSpotlight;

    public StatisticsViewModel(StatisticsService statisticsService, SpotlightService spotlightService, IOptions<UiOptions> uiOptions)
    {
        _statisticsService = statisticsService;
        _spotlightService = spotlightService;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(uiOptions.Value.VesselListRefreshMs)
        };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    private void Refresh()
    {
        var snap = _statisticsService.GetSnapshot();

        TotalVessels = snap.TotalVessels;
        CargoCount = snap.VesselCountByType.GetValueOrDefault(VesselType.Cargo);
        TankerCount = snap.VesselCountByType.GetValueOrDefault(VesselType.Tanker);
        PassengerCount = snap.VesselCountByType.GetValueOrDefault(VesselType.Passenger);
        FishingCount = snap.VesselCountByType.GetValueOrDefault(VesselType.Fishing);
        OtherCount = snap.TotalVessels - CargoCount - TankerCount - PassengerCount - FishingCount;
        AverageSpeedText = $"{snap.AverageSpeed:F1} kn";
        UnderWayCount = snap.VesselsUnderWay;
        AtAnchorCount = snap.VesselsAtAnchor;
        MooredCount = snap.VesselsMoored;
        WatchedCount = snap.WatchedVesselCount;

        // Update spotlight
        var (spotlightVessel, spotlightReason) = _spotlightService.GetSpotlight();
        HasSpotlight = spotlightVessel is not null;
        SpotlightVesselName = spotlightVessel?.DisplayName ?? "";
        SpotlightReason = spotlightReason;

        var dests = snap.TopDestinations;
        TopDestination1 = FormatDest(dests, 0);
        TopDestination2 = FormatDest(dests, 1);
        TopDestination3 = FormatDest(dests, 2);
        TopDestination4 = FormatDest(dests, 3);
        TopDestination5 = FormatDest(dests, 4);
    }

    private static string FormatDest(List<(string Destination, int Count)> dests, int index) =>
        index < dests.Count ? $"{dests[index].Destination} ({dests[index].Count})" : "";

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}
