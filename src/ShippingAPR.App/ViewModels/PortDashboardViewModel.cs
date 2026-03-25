using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class PortDashboardViewModel : ObservableObject, IDisposable
{
    private readonly PortActivityService _portActivityService;
    private readonly IPortRepository _portRepository;
    private readonly ITideDataClient _tideDataClient;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty]
    private string _selectedPort = "";

    [ObservableProperty]
    private int _vesselsInPort;

    [ObservableProperty]
    private int _arrivalsToday;

    [ObservableProperty]
    private int _departuresToday;

    [ObservableProperty]
    private string _congestionText = "---";

    [ObservableProperty]
    private string _congestionColor = "#808080";

    [ObservableProperty]
    private double _congestionPercent;

    // Tide data
    [ObservableProperty]
    private string _tideStateText = "---";

    [ObservableProperty]
    private string _tideStateIcon = "";

    [ObservableProperty]
    private string _currentTideLevelText = "---";

    [ObservableProperty]
    private string _nextHighTideText = "---";

    [ObservableProperty]
    private string _nextLowTideText = "---";

    [ObservableProperty]
    private string _tideColor = "#808080";

    public ObservableCollection<string> AvailablePorts { get; } = [];
    public ObservableCollection<PortActivityItemViewModel> RecentActivity { get; } = [];

    public PortDashboardViewModel(
        PortActivityService portActivityService,
        IPortRepository portRepository,
        ITideDataClient tideDataClient)
    {
        _portActivityService = portActivityService;
        _portRepository = portRepository;
        _tideDataClient = tideDataClient;

        // Pre-populate port list
        foreach (var port in _portRepository.GetAll().OrderBy(p => p.Name))
            AvailablePorts.Add(port.Name);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    partial void OnSelectedPortChanged(string value)
    {
        _portActivityService.SelectedPortName = value;
        Refresh();
        _ = RefreshTideAsync();
    }

    [RelayCommand]
    private void SelectPort(string portName)
    {
        SelectedPort = portName;
    }

    private void Refresh()
    {
        if (string.IsNullOrEmpty(SelectedPort)) return;

        var snapshot = _portActivityService.GetCongestion(SelectedPort);

        VesselsInPort = snapshot.VesselsInPort;
        ArrivalsToday = snapshot.ArrivalsLast24h;
        DeparturesToday = snapshot.DeparturesLast24h;
        CongestionPercent = snapshot.CongestionScore * 100;

        (CongestionText, CongestionColor) = snapshot.CongestionScore switch
        {
            < 0.25 => ("Low", "#4CAF50"),
            < 0.5 => ("Moderate", "#FFC107"),
            < 0.75 => ("High", "#FF9800"),
            _ => ("Very High", "#F44336")
        };

        RecentActivity.Clear();
        foreach (var record in snapshot.RecentActivity.Take(15))
        {
            RecentActivity.Add(new PortActivityItemViewModel
            {
                VesselName = record.VesselName,
                ActivityType = record.ActivityType == PortActivityType.Arrival ? "ARR" : "DEP",
                TimeText = record.Timestamp.ToString("HH:mm"),
                SpeedText = record.SpeedKnots.HasValue ? $"{record.SpeedKnots:F1} kn" : "---",
                IsArrival = record.ActivityType == PortActivityType.Arrival
            });
        }
    }

    private async Task RefreshTideAsync()
    {
        if (string.IsNullOrEmpty(SelectedPort)) return;

        var port = _portRepository.GetAll().FirstOrDefault(p => p.Name == SelectedPort);
        if (port is null) return;

        var tide = await _tideDataClient.GetTideDataAsync(port.Locode, port.Name, port.Latitude, port.Longitude);
        if (tide is null)
        {
            TideStateText = "---";
            TideStateIcon = "";
            CurrentTideLevelText = "---";
            NextHighTideText = "---";
            NextLowTideText = "---";
            return;
        }

        TideStateText = tide.State.ToString();
        TideStateIcon = tide.State switch
        {
            TideState.Rising => "\u2191",
            TideState.Falling => "\u2193",
            TideState.Slack => "\u2194",
            _ => ""
        };
        TideColor = tide.State switch
        {
            TideState.Rising => "#4CAF50",
            TideState.Falling => "#F44336",
            TideState.Slack => "#FFC107",
            _ => "#808080"
        };
        CurrentTideLevelText = $"{tide.CurrentLevelMeters:F1}m";
        NextHighTideText = tide.NextHighTide?.ToString("HH:mm") ?? "---";
        NextLowTideText = tide.NextLowTide?.ToString("HH:mm") ?? "---";
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}

public sealed class PortActivityItemViewModel
{
    public required string VesselName { get; init; }
    public required string ActivityType { get; init; }
    public required string TimeText { get; init; }
    public required string SpeedText { get; init; }
    public required bool IsArrival { get; init; }

    public string ActivityColor => IsArrival ? "#4CAF50" : "#F44336";
    public string ActivityIcon => IsArrival ? "\u2193" : "\u2191";
}
