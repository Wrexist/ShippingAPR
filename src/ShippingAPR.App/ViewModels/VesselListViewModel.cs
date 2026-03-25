using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using ShippingAPR.App.Configuration;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.App.ViewModels;

public partial class VesselListViewModel : ObservableObject, IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly IWatchlistService _watchlistService;
    private readonly FilterViewModel _filterViewModel;
    private readonly DispatcherTimer _refreshTimer;
    private readonly EventHandler<Vessel> _onVesselAdded;
    private readonly EventHandler<Vessel> _onVesselUpdated;
    private readonly EventHandler _onStoreCleared;
    private readonly PropertyChangedEventHandler _onFilterChanged;

    [ObservableProperty]
    private Vessel? _selectedVessel;

    [ObservableProperty]
    private VesselListItem? _selectedListItem;

    [ObservableProperty]
    private string _sortBy = "Name";

    [ObservableProperty]
    private bool _isEmpty;

    public ObservableCollection<VesselListItem> Vessels { get; } = [];

    public VesselListViewModel(IVesselStore vesselStore, IWatchlistService watchlistService, FilterViewModel filterViewModel, IOptions<UiOptions> uiOptions)
    {
        _vesselStore = vesselStore;
        _watchlistService = watchlistService;
        _filterViewModel = filterViewModel;

        _onVesselAdded = (_, _) => ScheduleRefresh();
        _onVesselUpdated = (_, _) => ScheduleRefresh();
        _onStoreCleared = (_, _) => Application.Current?.Dispatcher.Invoke(() => { Vessels.Clear(); IsEmpty = true; });
        _onFilterChanged = (_, _) => ScheduleRefresh();

        _filterViewModel.PropertyChanged += _onFilterChanged;
        _vesselStore.VesselAdded += _onVesselAdded;
        _vesselStore.VesselUpdated += _onVesselUpdated;
        _vesselStore.StoreCleared += _onStoreCleared;

        // Refresh list periodically (not on every update — too expensive)
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(uiOptions.Value.VesselListRefreshMs)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshList();
        };
    }

    partial void OnSelectedListItemChanged(VesselListItem? value)
    {
        SelectedVessel = value is not null ? _vesselStore.GetByMmsi(value.Mmsi) : null;
    }

    private void ScheduleRefresh()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!_refreshTimer.IsEnabled)
                _refreshTimer.Start();
        });
    }

    private void RefreshList()
    {
        var vessels = _vesselStore.Vessels.Values
            .Where(v => v.CurrentPosition is not null)
            .Where(v => _filterViewModel.ShouldShow(v.Type, v.CurrentPosition!.SpeedOverGround))
            .Select(v => new VesselListItem
            {
                Mmsi = v.Mmsi,
                Name = v.DisplayName,
                Type = v.Type,
                Speed = v.CurrentPosition?.SpeedOverGround ?? 0,
                Destination = v.StaticData?.Destination ?? "",
                EtaHours = v.CalculatedEta?.TimeToArrival.TotalHours,
                DistanceNm = v.CalculatedEta?.DistanceNauticalMiles,
                CountryCode = v.StaticData?.CountryCode ?? "",
                LastUpdated = v.LastUpdated,
                IsWatched = _watchlistService.IsWatched(v.Mmsi)
            })
            .OrderBy(v => SortBy switch
            {
                "Speed" => (IComparable)v.Speed,
                "ETA" => (IComparable)(v.EtaHours ?? double.MaxValue),
                "Distance" => (IComparable)(v.DistanceNm ?? double.MaxValue),
                _ => v.Name
            })
            .ToList();

        // Efficient dictionary-based diff: update existing, add new, remove stale
        var newByMmsi = new Dictionary<int, VesselListItem>(vessels.Count);
        foreach (var v in vessels)
            newByMmsi[v.Mmsi] = v;

        // Remove stale (reverse iteration for safe removal)
        for (int i = Vessels.Count - 1; i >= 0; i--)
        {
            if (!newByMmsi.ContainsKey(Vessels[i].Mmsi))
                Vessels.RemoveAt(i);
        }

        // Update or add — use dictionary for O(1) lookup
        var existingByMmsi = new Dictionary<int, VesselListItem>(Vessels.Count);
        foreach (var v in Vessels)
            existingByMmsi[v.Mmsi] = v;

        foreach (var item in vessels)
        {
            if (existingByMmsi.TryGetValue(item.Mmsi, out var existing))
            {
                existing.Name = item.Name;
                existing.Speed = item.Speed;
                existing.Destination = item.Destination;
                existing.EtaHours = item.EtaHours;
                existing.DistanceNm = item.DistanceNm;
                existing.IsWatched = item.IsWatched;
            }
            else
            {
                Vessels.Add(item);
            }
        }

        IsEmpty = Vessels.Count == 0;
    }

    public void SelectVesselByMmsi(int mmsi)
    {
        SelectedVessel = _vesselStore.GetByMmsi(mmsi);
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _filterViewModel.PropertyChanged -= _onFilterChanged;
        _vesselStore.VesselAdded -= _onVesselAdded;
        _vesselStore.VesselUpdated -= _onVesselUpdated;
        _vesselStore.StoreCleared -= _onStoreCleared;
    }
}

public partial class VesselListItem : ObservableObject
{
    public int Mmsi { get; init; }

    [ObservableProperty]
    private string _name = "";

    public VesselType Type { get; init; }

    [ObservableProperty]
    private double _speed;

    [ObservableProperty]
    private string _destination = "";

    [ObservableProperty]
    private double? _etaHours;

    [ObservableProperty]
    private double? _distanceNm;

    [ObservableProperty]
    private bool _isWatched;

    public string CountryCode { get; init; } = "";
    public DateTime LastUpdated { get; init; }

    public string SpeedText => $"{Speed:F1} kn";

    public string EtaText
    {
        get
        {
            if (EtaHours is null || double.IsNaN(EtaHours.Value) || double.IsInfinity(EtaHours.Value) || EtaHours < 0)
                return "--";
            if (EtaHours >= 24)
                return $"{(int)(EtaHours / 24)}d {(int)(EtaHours % 24)}h";
            if (EtaHours >= 1)
                return $"{(int)EtaHours}h {(int)((EtaHours % 1) * 60)}m";
            return $"{(int)(EtaHours * 60)}m";
        }
    }

    public string DistanceText =>
        DistanceNm is not null ? $"{DistanceNm:F0} NM" : "--";
}
