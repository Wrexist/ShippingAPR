using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class GeofenceViewModel : ObservableObject, IDisposable
{
    private readonly AreaMonitorService _areaMonitorService;
    private readonly MapViewModel _mapViewModel;
    private readonly EventHandler _onGeofencesChanged;

    [ObservableProperty] private string _newZoneName = string.Empty;
    [ObservableProperty] private bool _alertOnEntry = true;
    [ObservableProperty] private bool _alertOnExit = true;

    public ObservableCollection<GeofenceZone> Zones { get; } = new();

    public GeofenceViewModel(
        AreaMonitorService areaMonitorService,
        MapViewModel mapViewModel)
    {
        _areaMonitorService = areaMonitorService;
        _mapViewModel = mapViewModel;

        _onGeofencesChanged = (_, _) => RefreshZones();
        _areaMonitorService.GeofencesChanged += _onGeofencesChanged;
        RefreshZones();
    }

    private bool CanAddZone => !string.IsNullOrWhiteSpace(NewZoneName);

    partial void OnNewZoneNameChanged(string value) => AddFromViewportCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanAddZone))]
    private void AddFromViewport()
    {
        var name = NewZoneName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var bounds = _mapViewModel.SelectedArea;

        var zone = new GeofenceZone
        {
            Name = name,
            Bounds = bounds,
            AlertOnEntry = AlertOnEntry,
            AlertOnExit = AlertOnExit,
            Color = "#4488FF"
        };

        _areaMonitorService.AddGeofence(zone);
        NewZoneName = string.Empty;
    }

    [RelayCommand]
    private void RemoveZone(string name)
    {
        _areaMonitorService.RemoveGeofence(name);
    }

    private void RefreshZones()
    {
        Zones.Clear();
        foreach (var zone in _areaMonitorService.Geofences)
            Zones.Add(zone);
    }

    public void Dispose() => _areaMonitorService.GeofencesChanged -= _onGeofencesChanged;
}
