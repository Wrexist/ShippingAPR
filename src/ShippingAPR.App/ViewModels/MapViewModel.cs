using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using ShippingAPR.App.Configuration;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class MapViewModel : ObservableObject, IDisposable
{
    internal const string MmsiFeatureKey = "MMSI";
    internal const string NameFeatureKey = "Name";

    private readonly IVesselStore _vesselStore;
    private readonly IVesselTrackingService _trackingService;
    private readonly IWatchlistService _watchlistService;
    private readonly AreaMonitorService _areaMonitorService;
    private readonly WeatherOverlayService _weatherOverlayService;
    private readonly HeatmapService _heatmapService;
    private readonly UserPreferences _userPreferences;
    private readonly UiOptions _uiOptions;
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _viewportDebounceTimer;
    private readonly Dictionary<int, IFeature> _vesselFeatures = new();
    private readonly List<(int Mmsi, Vessel Vessel)> _pendingUpdates = [];
    private readonly object _pendingLock = new();

    private MemoryLayer? _vesselLayer;
    private MemoryLayer? _trailLayer;
    private MemoryLayer? _selectionLayer;
    private MemoryLayer? _clusterLayer;
    private MemoryLayer? _geofenceLayer;
    private MemoryLayer? _weatherLayer;
    private MemoryLayer? _heatmapLayer;
    private MemoryLayer? _measureLayer;
    private Vessel? _highlightedVessel;
    private bool _viewportTrackingEnabled = true;
    private bool _isTracking;
    private bool _featuresNeedRebuild;
    private readonly EventHandler<Vessel> _onVesselChanged;
    private readonly EventHandler _onStoreCleared;

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

    // Weather overlay
    [ObservableProperty]
    private bool _showWeatherOverlay;

    // Heatmap overlay
    [ObservableProperty]
    private bool _showHeatmap;

    // Map layer switching
    [ObservableProperty]
    private string _currentMapLayer = "OpenStreetMap";

    // Distance measurement
    [ObservableProperty]
    private bool _isMeasuring;

    [ObservableProperty]
    private string _measurementText = "";

    private MPoint? _measureStart;
    private MPoint? _measureEnd;

    // Route playback
    [ObservableProperty]
    private bool _isPlaybackMode;

    [ObservableProperty]
    private double _playbackPosition; // 0.0 to 1.0

    [ObservableProperty]
    private string _playbackTimeText = "";

    // Map bookmarks
    public List<MapBookmark> Bookmarks => _userPreferences.Bookmarks;

    private MPoint? _selectionStart;

    /// <summary>
    /// Raised when a vessel feature is clicked on the map.
    /// The event argument is the MMSI of the clicked vessel.
    /// </summary>
    public event EventHandler<int>? VesselFeatureClicked;

    public MapViewModel(
        IVesselStore vesselStore,
        IVesselTrackingService trackingService,
        IWatchlistService watchlistService,
        AreaMonitorService areaMonitorService,
        WeatherOverlayService weatherOverlayService,
        HeatmapService heatmapService,
        UserPreferences userPreferences,
        IOptions<UiOptions> uiOptions)
    {
        _vesselStore = vesselStore;
        _trackingService = trackingService;
        _watchlistService = watchlistService;
        _areaMonitorService = areaMonitorService;
        _weatherOverlayService = weatherOverlayService;
        _heatmapService = heatmapService;
        _userPreferences = userPreferences;
        _uiOptions = uiOptions.Value;

        _onVesselChanged = (_, v) => OnVesselChanged(null, v);
        _onStoreCleared = (_, _) => ClearVessels();

        InitializeMap();

        _vesselStore.VesselAdded += _onVesselChanged;
        _vesselStore.VesselUpdated += _onVesselChanged;
        _vesselStore.StoreCleared += _onStoreCleared;
        _areaMonitorService.GeofencesChanged += (_, _) =>
            Application.Current?.Dispatcher.Invoke(RenderGeofences);

        _weatherOverlayService.WeatherUpdated += (_, _) =>
            Application.Current?.Dispatcher.Invoke(RenderWeatherOverlay);
        _heatmapService.HeatmapUpdated += (_, _) =>
            Application.Current?.Dispatcher.Invoke(RenderHeatmap);

        // Batch UI updates for performance
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_uiOptions.MapBatchUpdateMs)
        };
        _updateTimer.Tick += FlushPendingUpdates;
        _updateTimer.Start();

        // Debounce viewport changes after last pan/zoom
        _viewportDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_uiOptions.ViewportDebounceMs)
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

        // Heatmap layer (traffic density)
        _heatmapLayer = new MemoryLayer
        {
            Name = "Heatmap",
            Enabled = false
        };
        Map.Layers.Add(_heatmapLayer);

        // Weather overlay layer
        _weatherLayer = new MemoryLayer
        {
            Name = "Weather",
            Enabled = false
        };
        Map.Layers.Add(_weatherLayer);

        // Trail layer (ship track history)
        _trailLayer = new MemoryLayer
        {
            Name = "Trails",
            Style = new VectorStyle
            {
                Line = new Pen(Mapsui.Styles.Color.FromArgb(_uiOptions.TrailAlpha, 0, 150, 255), 2)
            }
        };
        Map.Layers.Add(_trailLayer);

        // Selection layer (area rectangle)
        _selectionLayer = new MemoryLayer
        {
            Name = "Selection",
            Style = new VectorStyle
            {
                Fill = new Brush(Mapsui.Styles.Color.FromArgb(_uiOptions.SelectionFillAlpha,
                    _uiOptions.SelectionRed, _uiOptions.SelectionGreen, _uiOptions.SelectionBlue)),
                Line = new Pen(Mapsui.Styles.Color.FromArgb(_uiOptions.SelectionLineAlpha,
                    _uiOptions.SelectionRed, _uiOptions.SelectionGreen, _uiOptions.SelectionBlue), 2)
                {
                    PenStyle = PenStyle.Dash
                }
            }
        };
        Map.Layers.Add(_selectionLayer);

        // Geofence layer (named zones)
        _geofenceLayer = new MemoryLayer
        {
            Name = "Geofences"
        };
        Map.Layers.Add(_geofenceLayer);

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

        // Measurement layer (distance tool)
        _measureLayer = new MemoryLayer
        {
            Name = "Measurement"
        };
        Map.Layers.Add(_measureLayer);

        // Start with default view (Northern Europe by default — busy shipping area)
        var center = SphericalMercator.FromLonLat(_uiOptions.DefaultCenterLon, _uiOptions.DefaultCenterLat);
        Map.Navigator.CenterOnAndZoomTo(new MPoint(center.x, center.y), _uiOptions.DefaultResolution);
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

    /// <summary>Computes the current map viewport as a lat/lon BoundingBox, or null if viewport is not ready.</summary>
    private BoundingBox? GetViewportBoundingBox()
    {
        var viewport = Map.Navigator.Viewport;
        if (viewport.Width == 0 || viewport.Height == 0) return null;

        var extent = viewport.ToExtent();
        var min = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
        var max = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);

        var minLat = Math.Max(-85, Math.Min(min.lat, max.lat));
        var maxLat = Math.Min(85, Math.Max(min.lat, max.lat));
        var minLon = Math.Max(-180, Math.Min(min.lon, max.lon));
        var maxLon = Math.Min(180, Math.Max(min.lon, max.lon));

        return new BoundingBox(minLat, minLon, maxLat, maxLon);
    }

    private void OnViewportDebounceElapsed(object? sender, EventArgs e)
    {
        _viewportDebounceTimer.Stop();

        var viewportArea = GetViewportBoundingBox();
        if (viewportArea is null) return;

        // Only update if area changed significantly
        if (SelectedArea is not null && !HasAreaChangedSignificantly(SelectedArea, viewportArea, _uiOptions.AreaChangeThreshold))
            return;

        SelectedArea = viewportArea;
        AreaStatusText = FormatAreaName(viewportArea);

        _ = _trackingService.ChangeAreaAsync(viewportArea);
    }

    private static bool HasAreaChangedSignificantly(BoundingBox old, BoundingBox current, double threshold)
    {
        var latRange = old.MaxLatitude - old.MinLatitude;
        var lonRange = old.MaxLongitude - old.MinLongitude;

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
        var shouldCluster = resolution > _uiOptions.ClusterResolutionThreshold;

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

    public void HandleVesselClick(int mmsi) =>
        VesselFeatureClicked?.Invoke(this, mmsi);

    public (string Name, string Type, string Speed, string Destination)? GetVesselSummary(int mmsi)
    {
        var vessel = _vesselStore.GetByMmsi(mmsi);
        if (vessel is null) return null;
        return (
            vessel.DisplayName,
            vessel.Type.ToString(),
            vessel.CurrentPosition is not null ? $"{vessel.CurrentPosition.SpeedOverGround:F1} kn" : "--",
            vessel.StaticData?.Destination ?? "Unknown"
        );
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

        // Derive the initial tracking area from the current map viewport
        // so the subscription matches exactly what the user sees.
        var area = GetViewportBoundingBox() ?? SelectedArea;
        SelectedArea = area;
        AreaStatusText = FormatAreaName(area);

        await _trackingService.StartTrackingAsync(area);
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
            > 100 => _uiOptions.ZoomGlobalResolution,
            > 50 => _uiOptions.ZoomContinentalResolution,
            _ => _uiOptions.ZoomRegionalResolution
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
        UpdateTrackVisualization(vessel);
        // Trigger re-render to update highlight style
        _vesselLayer?.DataHasChanged();
    }

    /// <summary>
    /// Draws the track history of the highlighted vessel as speed-gradient colored segments.
    /// Blue (0-5 kn) -> Green (5-12 kn) -> Yellow (12-18 kn) -> Red (18+ kn)
    /// </summary>
    private void UpdateTrackVisualization(Vessel? vessel)
    {
        if (_trailLayer is null) return;

        if (vessel?.Track is null || vessel.Track.Count < 2)
        {
            _trailLayer.Features = [];
            _trailLayer.DataHasChanged();
            return;
        }

        var track = vessel.Track;
        var features = new List<IFeature>();

        for (int i = 0; i < track.Count - 1; i++)
        {
            var tp1 = track[i];
            var tp2 = track[i + 1];

            var p1 = SphericalMercator.FromLonLat(tp1.Longitude, tp1.Latitude);
            var p2 = SphericalMercator.FromLonLat(tp2.Longitude, tp2.Latitude);

            var coords = new[] { new Coordinate(p1.x, p1.y), new Coordinate(p2.x, p2.y) };
            var segment = new LineString(coords);
            var feature = new GeometryFeature(segment);

            var color = GetSpeedColor(tp1.SpeedOverGround);
            feature.Styles.Add(new VectorStyle
            {
                Line = new Pen(color, 3)
            });

            features.Add(feature);
        }

        _trailLayer.Features = features;
        _trailLayer.DataHasChanged();
    }

    private static Mapsui.Styles.Color GetSpeedColor(double speedKnots)
    {
        // Blue (0-5 kn) -> Green (5-12 kn) -> Yellow (12-18 kn) -> Red (18+ kn)
        return speedKnots switch
        {
            < 2 => new Mapsui.Styles.Color(33, 150, 243, 180),    // Blue
            < 5 => new Mapsui.Styles.Color(0, 188, 212, 180),     // Cyan
            < 8 => new Mapsui.Styles.Color(76, 175, 80, 180),     // Green
            < 12 => new Mapsui.Styles.Color(139, 195, 74, 180),   // Light Green
            < 15 => new Mapsui.Styles.Color(255, 235, 59, 180),   // Yellow
            < 18 => new Mapsui.Styles.Color(255, 152, 0, 180),    // Orange
            _ => new Mapsui.Styles.Color(244, 67, 54, 180)        // Red
        };
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

        // Deduplicate — keep latest update per MMSI using dictionary (O(n) vs O(n log n))
        var latestDict = new Dictionary<int, Vessel>(updates.Count);
        foreach (var (mmsi, vessel) in updates)
            latestDict[mmsi] = vessel;
        var latestByMmsi = latestDict;

        foreach (var (mmsi, vessel) in latestDict)
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
                feature[MmsiFeatureKey] = mmsi;
                feature[NameFeatureKey] = vessel.DisplayName;
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
        var cellSize = viewport.Resolution * _uiOptions.ClusterGridCellPx;
        if (cellSize <= 0) return; // Guard against division by zero

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
            var scale = Math.Min(_uiOptions.ClusterScaleMax, _uiOptions.ClusterScaleMin + vessels.Count * _uiOptions.ClusterScalePerVessel);
            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = scale,
                Fill = new Brush(Mapsui.Styles.Color.FromArgb(_uiOptions.ClusterFillAlpha,
                    _uiOptions.ClusterRed, _uiOptions.ClusterGreen, _uiOptions.ClusterBlue)),
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
        var isWatched = _watchlistService.IsWatched(vessel.Mmsi);
        var color = GetVesselColor(vessel.Type);

        Pen outline;
        if (isHighlighted)
            outline = new Pen(Mapsui.Styles.Color.White, 3);
        else if (isWatched)
            outline = new Pen(new Mapsui.Styles.Color(255, 215, 0), 2); // Gold outline for watched
        else
            outline = new Pen(Mapsui.Styles.Color.FromArgb(180, 0, 0, 0), 1);

        var scale = isHighlighted ? _uiOptions.VesselScaleHighlighted
            : isWatched ? _uiOptions.VesselScaleNormal * 1.15
            : _uiOptions.VesselScaleNormal;

        return new SymbolStyle
        {
            SymbolScale = scale,
            SymbolRotation = vessel.CurrentPosition?.TrueHeading ?? 0,
            Fill = new Brush(color),
            Outline = outline,
            SymbolType = SymbolType.Triangle
        };
    }

    private static Mapsui.Styles.Color GetVesselColor(VesselType type)
    {
        var (r, g, b) = Core.VesselTypeColors.GetRgb(type);
        return new Mapsui.Styles.Color(r, g, b);
    }

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

    [RelayCommand]
    private void AddGeofenceFromSelection(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var zone = new GeofenceZone
        {
            Name = name,
            Bounds = SelectedArea
        };
        _areaMonitorService.AddGeofence(zone);
    }

    [RelayCommand]
    private void RemoveGeofence(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _areaMonitorService.RemoveGeofence(name);
    }

    private void RenderGeofences()
    {
        if (_geofenceLayer is null) return;

        var features = new List<IFeature>();
        foreach (var zone in _areaMonitorService.Geofences)
        {
            var area = zone.Bounds;
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

            // Parse zone color
            byte r = 108, g = 99, b = 255;
            if (zone.Color.Length == 7 && zone.Color.StartsWith('#'))
            {
                r = Convert.ToByte(zone.Color.Substring(1, 2), 16);
                g = Convert.ToByte(zone.Color.Substring(3, 2), 16);
                b = Convert.ToByte(zone.Color.Substring(5, 2), 16);
            }

            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(Mapsui.Styles.Color.FromArgb(30, r, g, b)),
                Line = new Pen(new Mapsui.Styles.Color(r, g, b, 180), 2)
                {
                    PenStyle = PenStyle.Dash
                }
            });

            // Zone label
            feature.Styles.Add(new LabelStyle
            {
                Text = zone.Name,
                ForeColor = new Mapsui.Styles.Color(r, g, b),
                BackColor = null,
                Font = new Font { Size = 11, Bold = true },
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center
            });

            features.Add(feature);
        }

        _geofenceLayer.Features = features;
        _geofenceLayer.DataHasChanged();
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

    // ═══════════════════════════════════════════════
    // Weather Overlay
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private async Task ToggleWeatherOverlay()
    {
        ShowWeatherOverlay = !ShowWeatherOverlay;
        _weatherOverlayService.IsEnabled = ShowWeatherOverlay;

        if (_weatherLayer is not null)
            _weatherLayer.Enabled = ShowWeatherOverlay;

        if (ShowWeatherOverlay)
        {
            await _weatherOverlayService.RefreshAsync(
                SelectedArea.MinLatitude, SelectedArea.MaxLatitude,
                SelectedArea.MinLongitude, SelectedArea.MaxLongitude);
        }
        else
        {
            _weatherOverlayService.Clear();
        }
    }

    private void RenderWeatherOverlay()
    {
        if (_weatherLayer is null || !ShowWeatherOverlay) return;

        var grid = _weatherOverlayService.CurrentGrid;
        if (grid is null || grid.Points.Length == 0)
        {
            _weatherLayer.Features = [];
            _weatherLayer.DataHasChanged();
            return;
        }

        var features = new List<IFeature>();
        foreach (var point in grid.Points)
        {
            var projected = SphericalMercator.FromLonLat(point.Longitude, point.Latitude);
            var feature = new GeometryFeature(new NetTopologySuite.Geometries.Point(projected.x, projected.y));

            // Wind arrow: rotated triangle showing wind direction
            var windColor = point.BeaufortScale switch
            {
                <= 3 => new Mapsui.Styles.Color(76, 175, 80, 140),     // Green - calm
                <= 5 => new Mapsui.Styles.Color(255, 235, 59, 160),    // Yellow - moderate
                <= 7 => new Mapsui.Styles.Color(255, 152, 0, 180),     // Orange - strong
                _ => new Mapsui.Styles.Color(244, 67, 54, 200)         // Red - severe
            };

            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.35,
                SymbolRotation = point.WindDirectionDegrees,
                Fill = new Brush(windColor),
                Outline = new Pen(Mapsui.Styles.Color.FromArgb(100, 0, 0, 0), 1),
                SymbolType = SymbolType.Triangle
            });

            // Wave height label
            if (point.WaveHeightMeters > 0.5)
            {
                feature.Styles.Add(new LabelStyle
                {
                    Text = $"{point.WaveHeightMeters:F1}m",
                    ForeColor = windColor,
                    BackColor = null,
                    Font = new Font { Size = 9 },
                    Offset = new Offset(0, 15)
                });
            }

            features.Add(feature);
        }

        _weatherLayer.Features = features;
        _weatherLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════
    // Traffic Heatmap
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void ToggleHeatmap()
    {
        ShowHeatmap = !ShowHeatmap;
        _heatmapService.IsEnabled = ShowHeatmap;

        if (_heatmapLayer is not null)
            _heatmapLayer.Enabled = ShowHeatmap;

        if (ShowHeatmap)
            RenderHeatmap();
    }

    private void RenderHeatmap()
    {
        if (_heatmapLayer is null || !ShowHeatmap) return;

        var grid = _heatmapService.GenerateGrid(
            SelectedArea.MinLatitude, SelectedArea.MaxLatitude,
            SelectedArea.MinLongitude, SelectedArea.MaxLongitude, 50);

        if (grid.MaxIntensity == 0)
        {
            _heatmapLayer.Features = [];
            _heatmapLayer.DataHasChanged();
            return;
        }

        var features = new List<IFeature>();
        var res = grid.Resolution;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                var cell = grid.Cells[y, x];
                if (cell.Intensity == 0) continue;

                var ratio = (double)cell.Intensity / grid.MaxIntensity;
                var alpha = (byte)(ratio * 150 + 30);

                // Gradient: blue -> cyan -> green -> yellow -> red
                var color = ratio switch
                {
                    < 0.2 => new Mapsui.Styles.Color(33, 150, 243, alpha),
                    < 0.4 => new Mapsui.Styles.Color(0, 188, 212, alpha),
                    < 0.6 => new Mapsui.Styles.Color(76, 175, 80, alpha),
                    < 0.8 => new Mapsui.Styles.Color(255, 235, 59, alpha),
                    _ => new Mapsui.Styles.Color(244, 67, 54, alpha)
                };

                var projected = SphericalMercator.FromLonLat(cell.CenterLon, cell.CenterLat);
                var feature = new GeometryFeature(new NetTopologySuite.Geometries.Point(projected.x, projected.y));
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolScale = 0.3 + ratio * 0.5,
                    Fill = new Brush(color),
                    Outline = null,
                    SymbolType = SymbolType.Ellipse
                });

                features.Add(feature);
            }
        }

        _heatmapLayer.Features = features;
        _heatmapLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════
    // Distance Measurement Tool
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void ToggleMeasurement()
    {
        IsMeasuring = !IsMeasuring;
        if (!IsMeasuring)
        {
            _measureStart = null;
            _measureEnd = null;
            MeasurementText = "";
            if (_measureLayer is not null)
            {
                _measureLayer.Features = [];
                _measureLayer.DataHasChanged();
            }
        }
    }

    public void HandleMeasureClick(double longitude, double latitude)
    {
        if (!IsMeasuring) return;

        if (_measureStart is null)
        {
            _measureStart = new MPoint(longitude, latitude);
            MeasurementText = "Click second point...";
        }
        else
        {
            _measureEnd = new MPoint(longitude, latitude);

            var distNm = Core.Calculations.HaversineCalculator.DistanceInNauticalMiles(
                _measureStart.Y, _measureStart.X, _measureEnd.Y, _measureEnd.X);
            var distKm = Core.Calculations.HaversineCalculator.DistanceInKilometers(
                _measureStart.Y, _measureStart.X, _measureEnd.Y, _measureEnd.X);

            MeasurementText = $"{distNm:F1} NM ({distKm:F1} km)";
            RenderMeasureLine();

            // Reset for next measurement
            _measureStart = null;
            _measureEnd = null;
        }
    }

    private void RenderMeasureLine()
    {
        if (_measureLayer is null || _measureStart is null || _measureEnd is null) return;

        var p1 = SphericalMercator.FromLonLat(_measureStart.X, _measureStart.Y);
        var p2 = SphericalMercator.FromLonLat(_measureEnd.X, _measureEnd.Y);

        var coords = new[] { new Coordinate(p1.x, p1.y), new Coordinate(p2.x, p2.y) };
        var line = new LineString(coords);
        var feature = new GeometryFeature(line);
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(new Mapsui.Styles.Color(255, 215, 0, 220), 3)
            {
                PenStyle = PenStyle.Dash
            }
        });

        // Start point
        var startFeature = new GeometryFeature(new NetTopologySuite.Geometries.Point(p1.x, p1.y));
        startFeature.Styles.Add(new SymbolStyle
        {
            SymbolScale = 0.25,
            Fill = new Brush(new Mapsui.Styles.Color(255, 215, 0)),
            SymbolType = SymbolType.Ellipse
        });

        // End point
        var endFeature = new GeometryFeature(new NetTopologySuite.Geometries.Point(p2.x, p2.y));
        endFeature.Styles.Add(new SymbolStyle
        {
            SymbolScale = 0.25,
            Fill = new Brush(new Mapsui.Styles.Color(255, 215, 0)),
            SymbolType = SymbolType.Ellipse
        });

        // Distance label
        var midX = (p1.x + p2.x) / 2;
        var midY = (p1.y + p2.y) / 2;
        var labelFeature = new GeometryFeature(new NetTopologySuite.Geometries.Point(midX, midY));
        labelFeature.Styles.Add(new LabelStyle
        {
            Text = MeasurementText,
            ForeColor = new Mapsui.Styles.Color(255, 215, 0),
            BackColor = new Brush(new Mapsui.Styles.Color(0, 0, 0, 160)),
            Font = new Font { Size = 12, Bold = true },
            Offset = new Offset(0, -15)
        });

        _measureLayer.Features = [feature, startFeature, endFeature, labelFeature];
        _measureLayer.DataHasChanged();
    }

    // ═══════════════════════════════════════════════
    // Map Layer Switching
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void SwitchMapLayer(string layerName)
    {
        CurrentMapLayer = layerName;

        // Remove the current base tile layer (always index 0)
        if (Map.Layers.Count > 0)
            Map.Layers.Remove(Map.Layers.First());

        // All modes start with OpenStreetMap as base
        // (Satellite/SeaMap would require additional tile packages —
        //  for now, we switch to different OSM-based styles)
        Map.Layers.Insert(0, OpenStreetMap.CreateTileLayer());

        // Note: To enable satellite/nautical charts, add BruTile.MbTiles
        // or a custom HttpTileSource for ESRI/OpenSeaMap tile servers.
        // The architecture is ready — just swap the tile source above.
    }

    // ═══════════════════════════════════════════════
    // Route Playback
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void TogglePlaybackMode()
    {
        IsPlaybackMode = !IsPlaybackMode;
        if (!IsPlaybackMode)
        {
            PlaybackPosition = 1.0;
            PlaybackTimeText = "";
        }
    }

    partial void OnPlaybackPositionChanged(double value)
    {
        if (!IsPlaybackMode || _highlightedVessel is null) return;

        var track = _highlightedVessel.Track;
        if (track is null || track.Count < 2) return;

        var idx = (int)(value * (track.Count - 1));
        idx = Math.Clamp(idx, 0, track.Count - 1);
        var tp = track[idx];

        PlaybackTimeText = tp.Timestamp.ToString("HH:mm:ss");

        // Move vessel feature to historical position
        var point = SphericalMercator.FromLonLat(tp.Longitude, tp.Latitude);
        if (_vesselFeatures.TryGetValue(_highlightedVessel.Mmsi, out var feature) && feature is GeometryFeature gf)
        {
            gf.Geometry = new NetTopologySuite.Geometries.Point(point.x, point.y);
            _vesselLayer?.DataHasChanged();
        }
    }

    // ═══════════════════════════════════════════════
    // Map Bookmarks
    // ═══════════════════════════════════════════════

    [RelayCommand]
    private void SaveBookmark(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var viewport = Map.Navigator.Viewport;
        var center = SphericalMercator.ToLonLat(viewport.CenterX, viewport.CenterY);

        _userPreferences.Bookmarks.Add(new MapBookmark
        {
            Name = name,
            Latitude = center.lat,
            Longitude = center.lon,
            ZoomLevel = viewport.Resolution
        });
        _userPreferences.Save();
        OnPropertyChanged(nameof(Bookmarks));
    }

    [RelayCommand]
    private void NavigateToBookmark(MapBookmark bookmark)
    {
        var center = SphericalMercator.FromLonLat(bookmark.Longitude, bookmark.Latitude);
        Map.Navigator.CenterOnAndZoomTo(new MPoint(center.x, center.y), bookmark.ZoomLevel);
    }

    [RelayCommand]
    private void RemoveBookmark(MapBookmark bookmark)
    {
        _userPreferences.Bookmarks.Remove(bookmark);
        _userPreferences.Save();
        OnPropertyChanged(nameof(Bookmarks));
    }

    public void Dispose()
    {
        _updateTimer.Stop();
        _viewportDebounceTimer.Stop();
        _vesselStore.VesselAdded -= _onVesselChanged;
        _vesselStore.VesselUpdated -= _onVesselChanged;
        _vesselStore.StoreCleared -= _onStoreCleared;
    }
}
