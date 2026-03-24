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
    private readonly DispatcherTimer _viewportDebounceTimer;
    private readonly Dictionary<int, IFeature> _vesselFeatures = new();
    private readonly List<(int Mmsi, Vessel Vessel)> _pendingUpdates = [];
    private readonly object _pendingLock = new();

    private MemoryLayer? _vesselLayer;
    private MemoryLayer? _trailLayer;
    private MemoryLayer? _selectionLayer;
    private MemoryLayer? _clusterLayer;
    private Vessel? _highlightedVessel;
    private bool _viewportTrackingEnabled = true;
    private bool _isTracking;
    private bool _featuresNeedRebuild;

    [ObservableProperty]
    private Map _map = new();

    [ObservableProperty]
    private bool _isSelectingArea;

    [ObservableProperty]
    private string _areaStatusText = "Zoom in to discover ships";

    [ObservableProperty]
    private BoundingBox _selectedArea = BoundingBox.NorthernEuropeDefault;

    [ObservableProperty]
    private bool _showClusters;

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

        // Debounce viewport changes — 500ms after last pan/zoom
        _viewportDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _viewportDebounceTimer.Tick += OnViewportDebounceElapsed;
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

        // Cluster layer (for low zoom levels)
        _clusterLayer = new MemoryLayer
        {
            Name = "Clusters",
            IsMapInfoLayer = true
        };
        Map.Layers.Add(_clusterLayer);

        // Vessel layer (ship positions)
        _vesselLayer = new MemoryLayer
        {
            Name = "Vessels",
            IsMapInfoLayer = true
        };
        Map.Layers.Add(_vesselLayer);

        // Start with Northern Europe view (busy shipping area)
        var center = SphericalMercator.FromLonLat(10.0, 54.0);
        // Resolution ~2446 = zoom level 6 (shows Northern Europe)
        Map.Navigator.CenterOnAndZoomTo(new MPoint(center.x, center.y), 2446);
    }

    /// <summary>Called by MapView when the viewport changes (pan/zoom).</summary>
    public void OnViewportChanged()
    {
        if (!_viewportTrackingEnabled || !_isTracking) return;

        // Restart debounce timer
        _viewportDebounceTimer.Stop();
        _viewportDebounceTimer.Start();

        // Update cluster visibility based on zoom level
        UpdateClusterVisibility();
    }

    private void OnViewportDebounceElapsed(object? sender, EventArgs e)
    {
        _viewportDebounceTimer.Stop();

        var viewport = Map.Navigator.Viewport;
        if (viewport.Width == 0 || viewport.Height == 0) return;

        // Convert viewport corners to lat/lon
        var min = SphericalMercator.ToLonLat(viewport.Extent.MinX, viewport.Extent.MinY);
        var max = SphericalMercator.ToLonLat(viewport.Extent.MaxX, viewport.Extent.MaxY);

        // Clamp to valid ranges
        var minLat = Math.Max(-85, Math.Min(min.lat, max.lat));
        var maxLat = Math.Min(85, Math.Max(min.lat, max.lat));
        var minLon = Math.Max(-180, Math.Min(min.lon, max.lon));
        var maxLon = Math.Min(180, Math.Max(min.lon, max.lon));

        var viewportArea = new BoundingBox(minLat, minLon, maxLat, maxLon);

        // Only update if area changed significantly (>10% shift)
        if (SelectedArea is not null && !HasAreaChangedSignificantly(SelectedArea, viewportArea))
            return;

        SelectedArea = viewportArea;
        AreaStatusText = FormatAreaName(viewportArea);

        _ = _trackingService.ChangeAreaAsync(viewportArea);
    }

    private static bool HasAreaChangedSignificantly(BoundingBox old, BoundingBox current)
    {
        var latRange = old.MaxLatitude - old.MinLatitude;
        var lonRange = old.MaxLongitude - old.MinLongitude;
        var threshold = 0.1; // 10%

        return Math.Abs(old.MinLatitude - current.MinLatitude) > latRange * threshold ||
               Math.Abs(old.MaxLatitude - current.MaxLatitude) > latRange * threshold ||
               Math.Abs(old.MinLongitude - current.MinLongitude) > lonRange * threshold ||
               Math.Abs(old.MaxLongitude - current.MaxLongitude) > lonRange * threshold;
    }

    private static string FormatAreaName(BoundingBox area)
    {
        var centerLat = (area.MinLatitude + area.MaxLatitude) / 2;
        var centerLon = (area.MinLongitude + area.MaxLongitude) / 2;

        // Simple region naming based on center coordinates
        var region = (centerLat, centerLon) switch
        {
            ( > 45 and < 70, > -15 and < 35) => "Northern Europe",
            ( > 30 and < 50, > -15 and < 35) => "Mediterranean",
            ( > 25 and < 50, > -85 and < -60) => "US East Coast",
            ( > 25 and < 50, > -130 and < -110) => "US West Coast",
            ( > -10 and < 25, > 95 and < 120) => "Southeast Asia",
            ( > 25 and < 45, > 110 and < 145) => "East Asia",
            ( > 20 and < 35, > 25 and < 45) => "Middle East",
            ( > -40 and < -10, > 110 and < 160) => "Australia",
            _ => $"{centerLat:F1}°{(centerLat >= 0 ? "N" : "S")}, {Math.Abs(centerLon):F1}°{(centerLon >= 0 ? "E" : "W")}"
        };

        return region;
    }

    private void UpdateClusterVisibility()
    {
        var resolution = Map.Navigator.Viewport.Resolution;
        var shouldCluster = resolution > 1000;

        if (shouldCluster != ShowClusters)
        {
            ShowClusters = shouldCluster;
            if (_vesselLayer is not null)
                _vesselLayer.Enabled = !shouldCluster;
            if (_clusterLayer is not null)
                _clusterLayer.Enabled = shouldCluster;
        }
    }

    [RelayCommand]
    private void ToggleAreaSelection()
    {
        IsSelectingArea = !IsSelectingArea;
        _selectionStart = null;

        // Disable viewport tracking when manually selecting area
        if (IsSelectingArea)
            _viewportTrackingEnabled = false;
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

            // Re-enable viewport tracking
            _viewportTrackingEnabled = true;

            // Start tracking the new area
            _ = _trackingService.ChangeAreaAsync(SelectedArea);
        }
    }

    [RelayCommand]
    private async Task StartTracking()
    {
        _isTracking = true;
        await _trackingService.StartTrackingAsync(SelectedArea);
    }

    /// <summary>Navigate to a preset region.</summary>
    [RelayCommand]
    private void NavigateToRegion(string region)
    {
        var box = region switch
        {
            "Europe" => BoundingBox.Europe,
            "Asia" => BoundingBox.Asia,
            "Americas" => BoundingBox.Americas,
            _ => BoundingBox.Global
        };

        var centerLon = (box.MinLongitude + box.MaxLongitude) / 2;
        var centerLat = (box.MinLatitude + box.MaxLatitude) / 2;
        var center = SphericalMercator.FromLonLat(centerLon, centerLat);

        // Calculate appropriate resolution for the region
        var latSpan = box.MaxLatitude - box.MinLatitude;
        var resolution = latSpan switch
        {
            > 100 => 9784,   // Global
            > 50 => 4892,    // Continental
            _ => 2446        // Regional
        };

        Map.Navigator.CenterOnAndZoomTo(new MPoint(center.x, center.y), resolution);
        AreaStatusText = region;
    }

    [RelayCommand]
    private void ZoomIn()
    {
        Map.Navigator.ZoomIn();
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Map.Navigator.ZoomOut();
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

        Map.Navigator.CenterOn(new MPoint(center.x, center.y));
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
                // Update existing feature position in-place
                if (existing is GeometryFeature gf)
                {
                    gf.Geometry = new NetTopologySuite.Geometries.Point(point.x, point.y);
                    gf.Styles.Clear();
                    gf.Styles.Add(CreateVesselStyle(vessel));
                }
            }
            else
            {
                var feature = new GeometryFeature(new NetTopologySuite.Geometries.Point(point.x, point.y));
                feature.Styles.Add(CreateVesselStyle(vessel));
                feature["MMSI"] = mmsi;
                feature["Name"] = vessel.DisplayName;
                _vesselFeatures[mmsi] = feature;
                _featuresNeedRebuild = true;
            }
        }

        // Only rebuild features list when new vessels were added (not on every update)
        if (_vesselLayer is not null)
        {
            if (_featuresNeedRebuild)
            {
                _vesselLayer.Features = _vesselFeatures.Values.ToList();
                _featuresNeedRebuild = false;
            }
            _vesselLayer.DataHasChanged();
        }

        // Update clusters if at low zoom
        if (ShowClusters)
        {
            UpdateClusters();
        }
    }

    private void UpdateClusters()
    {
        if (_clusterLayer is null) return;

        var viewport = Map.Navigator.Viewport;
        var cellSize = viewport.Resolution * 80; // 80px grid cells

        var clusters = new Dictionary<(int, int), List<Vessel>>();

        foreach (var (mmsi, feature) in _vesselFeatures)
        {
            var vessel = _vesselStore.GetByMmsi(mmsi);
            if (vessel?.CurrentPosition is null) continue;

            var point = SphericalMercator.FromLonLat(
                vessel.CurrentPosition.Longitude,
                vessel.CurrentPosition.Latitude);

            var cellX = (int)(point.x / cellSize);
            var cellY = (int)(point.y / cellSize);
            var key = (cellX, cellY);

            if (!clusters.TryGetValue(key, out var list))
            {
                list = [];
                clusters[key] = list;
            }
            list.Add(vessel);
        }

        var clusterFeatures = new List<IFeature>();
        foreach (var (key, vessels) in clusters)
        {
            if (vessels.Count == 0) continue;

            var avgLon = vessels.Average(v => v.CurrentPosition!.Longitude);
            var avgLat = vessels.Average(v => v.CurrentPosition!.Latitude);
            var center = SphericalMercator.FromLonLat(avgLon, avgLat);

            var feature = new GeometryFeature(new NetTopologySuite.Geometries.Point(center.x, center.y));
            var scale = Math.Min(1.0, 0.3 + vessels.Count * 0.05);
            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = scale,
                Fill = new Brush(Mapsui.Styles.Color.FromArgb(200, 108, 99, 255)),
                Outline = new Pen(Mapsui.Styles.Color.White, 2),
                SymbolType = SymbolType.Ellipse
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = vessels.Count.ToString(),
                ForeColor = Mapsui.Styles.Color.White,
                BackColor = null,
                Font = new Font { Size = 12, Bold = true },
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center
            });
            clusterFeatures.Add(feature);
        }

        _clusterLayer.Features = clusterFeatures;
        _clusterLayer.DataHasChanged();
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
            new Coordinate(min.x, min.y),
            new Coordinate(max.x, min.y),
            new Coordinate(max.x, max.y),
            new Coordinate(min.x, max.y),
            new Coordinate(min.x, min.y)
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
            _featuresNeedRebuild = true;
            if (_vesselLayer is not null)
            {
                _vesselLayer.Features = [];
                _vesselLayer.DataHasChanged();
            }
            if (_clusterLayer is not null)
            {
                _clusterLayer.Features = [];
                _clusterLayer.DataHasChanged();
            }
        });
    }
}
