using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using NetTopologySuite.Geometries;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.App.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly IVesselStore _vesselStore;
    private readonly IVesselTrackingService _trackingService;
    private readonly DispatcherTimer _updateTimer;
    private readonly Dictionary<int, IFeature> _vesselFeatures = new();
    private readonly List<(int Mmsi, Vessel Vessel)> _pendingUpdates = [];
    private readonly object _pendingLock = new();

    private MemoryLayer? _vesselLayer;
    private MemoryLayer? _trailLayer;
    private MemoryLayer? _selectionLayer;
    private Vessel? _highlightedVessel;

    [ObservableProperty]
    private Map _map = new();

    [ObservableProperty]
    private bool _isSelectingArea;

    [ObservableProperty]
    private string _areaStatusText = "Gothenburg (default)";

    [ObservableProperty]
    private BoundingBox _selectedArea = BoundingBox.GothenburgDefault;

    private MPoint? _selectionStart;

    public MapViewModel(
        IVesselStore vesselStore,
        IVesselTrackingService trackingService)
    {
        _vesselStore = vesselStore;
        _trackingService = trackingService;

        InitializeMap();

        _vesselStore.VesselAdded += OnVesselChanged;
        _vesselStore.VesselUpdated += OnVesselChanged;
        _vesselStore.StoreCleared += (_, _) => ClearVessels();

        // Batch UI updates every 250ms for performance
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _updateTimer.Tick += FlushPendingUpdates;
        _updateTimer.Start();
    }

    private void InitializeMap()
    {
        Map = new Map
        {
            CRS = "EPSG:3857"
        };

        // OpenStreetMap base layer
        Map.Layers.Add(OpenStreetMap.CreateTileLayer());

        // Trail layer (ship track history)
        _trailLayer = new MemoryLayer
        {
            Name = "Trails",
            Style = new VectorStyle
            {
                Line = new Pen(Mapsui.Styles.Color.FromArgb(128, 0, 150, 255), 2)
            }
        };
        Map.Layers.Add(_trailLayer);

        // Selection layer (area rectangle)
        _selectionLayer = new MemoryLayer
        {
            Name = "Selection",
            Style = new VectorStyle
            {
                Fill = new Brush(Mapsui.Styles.Color.FromArgb(40, 233, 69, 96)),
                Line = new Pen(Mapsui.Styles.Color.FromArgb(200, 233, 69, 96), 2)
                {
                    PenStyle = PenStyle.Dash
                }
            }
        };
        Map.Layers.Add(_selectionLayer);

        // Vessel layer (ship positions)
        _vesselLayer = new MemoryLayer
        {
            Name = "Vessels",
            IsMapInfoLayer = true
        };
        Map.Layers.Add(_vesselLayer);

        // Center on Gothenburg
        var center = SphericalMercator.FromLonLat(11.97, 57.71);
        Map.Home = n => n.CenterOnAndZoomTo(center, n.Resolutions[10]);

        // Draw default selection area
        UpdateSelectionOverlay(SelectedArea);
    }

    [RelayCommand]
    private void ToggleAreaSelection()
    {
        IsSelectingArea = !IsSelectingArea;
        _selectionStart = null;
    }

    public void HandleMapClick(double longitude, double latitude)
    {
        if (!IsSelectingArea) return;

        if (_selectionStart is null)
        {
            _selectionStart = new MPoint(longitude, latitude);
        }
        else
        {
            var minLat = Math.Min(_selectionStart.Y, latitude);
            var maxLat = Math.Max(_selectionStart.Y, latitude);
            var minLon = Math.Min(_selectionStart.X, longitude);
            var maxLon = Math.Max(_selectionStart.X, longitude);

            SelectedArea = new BoundingBox(minLat, minLon, maxLat, maxLon);
            UpdateSelectionOverlay(SelectedArea);

            AreaStatusText = $"{minLat:F2}°N, {minLon:F2}°E → {maxLat:F2}°N, {maxLon:F2}°E";

            IsSelectingArea = false;
            _selectionStart = null;

            // Start tracking the new area
            _ = _trackingService.ChangeAreaAsync(SelectedArea);
        }
    }

    [RelayCommand]
    private async Task StartTracking()
    {
        await _trackingService.StartTrackingAsync(SelectedArea);
    }

    public void HighlightVessel(Vessel? vessel)
    {
        _highlightedVessel = vessel;
        // Trigger re-render to update highlight style
        _vesselLayer?.DataHasChanged();
    }

    public void CenterOnVessel(Vessel? vessel)
    {
        if (vessel?.CurrentPosition is null) return;

        var center = SphericalMercator.FromLonLat(
            vessel.CurrentPosition.Longitude,
            vessel.CurrentPosition.Latitude);

        Map.Navigator.CenterOn(center);
        HighlightVessel(vessel);
    }

    private void OnVesselChanged(object? sender, Vessel vessel)
    {
        lock (_pendingLock)
        {
            _pendingUpdates.Add((vessel.Mmsi, vessel));
        }
    }

    private void FlushPendingUpdates(object? sender, EventArgs e)
    {
        List<(int Mmsi, Vessel Vessel)> updates;
        lock (_pendingLock)
        {
            if (_pendingUpdates.Count == 0) return;
            updates = [.. _pendingUpdates];
            _pendingUpdates.Clear();
        }

        // Deduplicate — keep latest update per MMSI
        var latestByMmsi = updates
            .GroupBy(u => u.Mmsi)
            .Select(g => g.Last())
            .ToList();

        foreach (var (mmsi, vessel) in latestByMmsi)
        {
            if (vessel.CurrentPosition is null) continue;

            var point = SphericalMercator.FromLonLat(
                vessel.CurrentPosition.Longitude,
                vessel.CurrentPosition.Latitude);

            if (_vesselFeatures.TryGetValue(mmsi, out var existing))
            {
                // Update existing feature position
                if (existing is GeometryFeature gf)
                {
                    gf.Geometry = new Point(point.X, point.Y);
                    gf.Styles.Clear();
                    gf.Styles.Add(CreateVesselStyle(vessel));
                }
            }
            else
            {
                var feature = new GeometryFeature(new Point(point.X, point.Y));
                feature.Styles.Add(CreateVesselStyle(vessel));
                feature["MMSI"] = mmsi;
                feature["Name"] = vessel.DisplayName;
                _vesselFeatures[mmsi] = feature;
            }
        }

        // Rebuild layer features
        if (_vesselLayer is not null)
        {
            _vesselLayer.Features = _vesselFeatures.Values.ToList();
            _vesselLayer.DataHasChanged();
        }
    }

    private SymbolStyle CreateVesselStyle(Vessel vessel)
    {
        var isHighlighted = _highlightedVessel?.Mmsi == vessel.Mmsi;
        var color = GetVesselColor(vessel.Type);

        return new SymbolStyle
        {
            SymbolScale = isHighlighted ? 0.6 : 0.4,
            SymbolRotation = vessel.CurrentPosition?.TrueHeading ?? 0,
            Fill = new Brush(color),
            Outline = isHighlighted
                ? new Pen(Mapsui.Styles.Color.White, 3)
                : new Pen(Mapsui.Styles.Color.FromArgb(180, 0, 0, 0), 1),
            SymbolType = SymbolType.Triangle
        };
    }

    private static Mapsui.Styles.Color GetVesselColor(VesselType type) => type switch
    {
        VesselType.Cargo => new Mapsui.Styles.Color(76, 175, 80),
        VesselType.Tanker => new Mapsui.Styles.Color(255, 87, 34),
        VesselType.Passenger => new Mapsui.Styles.Color(33, 150, 243),
        VesselType.Fishing => new Mapsui.Styles.Color(255, 152, 0),
        VesselType.Tug or VesselType.Pilot => new Mapsui.Styles.Color(156, 39, 176),
        VesselType.Military => new Mapsui.Styles.Color(96, 125, 139),
        VesselType.Sailing or VesselType.PleasureCraft => new Mapsui.Styles.Color(0, 188, 212),
        VesselType.HighSpeedCraft => new Mapsui.Styles.Color(255, 235, 59),
        VesselType.SearchAndRescue => new Mapsui.Styles.Color(244, 67, 54),
        _ => new Mapsui.Styles.Color(158, 158, 158)
    };

    private void UpdateSelectionOverlay(BoundingBox area)
    {
        var min = SphericalMercator.FromLonLat(area.MinLongitude, area.MinLatitude);
        var max = SphericalMercator.FromLonLat(area.MaxLongitude, area.MaxLatitude);

        var ring = new LinearRing([
            new Coordinate(min.X, min.Y),
            new Coordinate(max.X, min.Y),
            new Coordinate(max.X, max.Y),
            new Coordinate(min.X, max.Y),
            new Coordinate(min.X, min.Y)
        ]);

        var polygon = new Polygon(ring);
        var feature = new GeometryFeature(polygon);

        if (_selectionLayer is not null)
        {
            _selectionLayer.Features = [feature];
            _selectionLayer.DataHasChanged();
        }
    }

    private void ClearVessels()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _vesselFeatures.Clear();
            if (_vesselLayer is not null)
            {
                _vesselLayer.Features = [];
                _vesselLayer.DataHasChanged();
            }
        });
    }
}
