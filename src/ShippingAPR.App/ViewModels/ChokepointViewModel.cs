using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using ShippingAPR.App.Configuration;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class ChokepointViewModel : ObservableObject, IDisposable
{
    private readonly ChokepointMonitorService _monitorService;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<ChokepointItemViewModel> Chokepoints { get; } = [];

    public ChokepointViewModel(ChokepointMonitorService monitorService, IOptions<UiOptions> uiOptions)
    {
        _monitorService = monitorService;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    private void Refresh()
    {
        var statuses = _monitorService.GetAllStatuses();

        // Efficient update: match existing items or add new ones
        for (int i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            if (i < Chokepoints.Count)
            {
                Chokepoints[i].Update(status);
            }
            else
            {
                Chokepoints.Add(new ChokepointItemViewModel(status));
            }
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}

public partial class ChokepointItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _vesselsInTransit;
    [ObservableProperty] private int _transitsLast24h;
    [ObservableProperty] private string _avgSpeedText = "0 kn";
    [ObservableProperty] private string _congestionText = "Low";
    [ObservableProperty] private string _congestionColor = "#34D399";
    [ObservableProperty] private string _typicalTransitText = "";
    [ObservableProperty] private double _centerLatitude;
    [ObservableProperty] private double _centerLongitude;

    public ChokepointItemViewModel(ChokepointStatus status) => Update(status);

    public void Update(ChokepointStatus status)
    {
        Name = status.Name;
        VesselsInTransit = status.VesselsInTransit;
        TransitsLast24h = status.TransitsLast24h;
        AvgSpeedText = $"{status.AvgSpeedKnots:F1} kn";
        CongestionText = status.CongestionLevel.ToString();
        CongestionColor = status.CongestionLevel switch
        {
            ChokepointCongestion.Low => "#34D399",
            ChokepointCongestion.Moderate => "#FBBF24",
            ChokepointCongestion.High => "#FB923C",
            ChokepointCongestion.VeryHigh => "#EF4444",
            _ => "#9E9E9E"
        };
        var hours = status.TypicalTransitMinutes / 60;
        var mins = status.TypicalTransitMinutes % 60;
        TypicalTransitText = hours > 0 ? $"{hours}h {mins}m typical" : $"{mins}m typical";
        CenterLatitude = status.CenterLatitude;
        CenterLongitude = status.CenterLongitude;
    }
}
