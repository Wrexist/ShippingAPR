using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mapsui.UI;
using Mapsui.Projections;
using ShippingAPR.App.ViewModels;

namespace ShippingAPR.App.Views;

public partial class MapView : UserControl
{
    private MapViewModel? _currentViewModel;
    private EventHandler<MapInfoEventArgs>? _infoHandler;
    private EventHandler? _navigatedHandler;
    private int _lastHoveredMmsi;

    public MapView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe previous handlers to prevent leaks and double-registration
        if (_currentViewModel is not null)
        {
            if (_infoHandler is not null)
                MapControl.Info -= _infoHandler;
            if (_navigatedHandler is not null)
                _currentViewModel.Map.Navigator.Navigated -= _navigatedHandler;
            MapControl.MouseMove -= OnMapMouseMove;
            MapControl.MouseLeave -= OnMapMouseLeave;
        }

        if (e.NewValue is MapViewModel vm)
        {
            _currentViewModel = vm;
            MapControl.Map = vm.Map;

            _infoHandler = (_, args) =>
            {
                if (args.MapInfo?.WorldPosition is not null)
                {
                    var lonLat = SphericalMercator.ToLonLat(
                        args.MapInfo.WorldPosition.X,
                        args.MapInfo.WorldPosition.Y);

                    // Route click to measurement tool if active
                    if (vm.IsMeasuring)
                        vm.HandleMeasureClick(lonLat.lon, lonLat.lat);
                    else
                        vm.HandleMapClick(lonLat.lon, lonLat.lat);
                }

                // Delegate vessel click handling to ViewModel (no Window.GetWindow coupling)
                if (args.MapInfo?.Feature?[MapViewModel.MmsiFeatureKey] is int mmsi)
                {
                    vm.HandleVesselClick(mmsi);
                }
            };
            MapControl.Info += _infoHandler;

            // Wire viewport changes for dynamic area tracking
            _navigatedHandler = (_, _) => vm.OnViewportChanged();
            vm.Map.Navigator.Navigated += _navigatedHandler;

            // Wire hover tooltip
            MapControl.MouseMove += OnMapMouseMove;
            MapControl.MouseLeave += OnMapMouseLeave;
        }
        else
        {
            _currentViewModel = null;
            _infoHandler = null;
            _navigatedHandler = null;
        }
    }

    private void OnMapMouseMove(object sender, MouseEventArgs e)
    {
        if (_currentViewModel is null) return;

        var screenPos = e.GetPosition(MapControl);
        var mapInfo = MapControl.GetMapInfo(new Mapsui.MPoint(screenPos.X, screenPos.Y));

        if (mapInfo?.Feature?[MapViewModel.MmsiFeatureKey] is int mmsi)
        {
            if (mmsi != _lastHoveredMmsi)
            {
                _lastHoveredMmsi = mmsi;
                var summary = _currentViewModel.GetVesselSummary(mmsi);
                if (summary.HasValue)
                {
                    TooltipName.Text = summary.Value.Name;
                    TooltipType.Text = summary.Value.Type;
                    TooltipSpeed.Text = summary.Value.Speed;
                    TooltipDestination.Text = summary.Value.Destination;

                    // Set type color indicator
                    var (r, g, b) = Core.VesselTypeColors.GetRgb(
                        _currentViewModel.GetVesselSummary(mmsi) is var s
                            ? Enum.TryParse<Core.Enums.VesselType>(summary.Value.Type, out var vt) ? vt : Core.Enums.VesselType.Unknown
                            : Core.Enums.VesselType.Unknown);
                    TooltipTypeIndicator.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));

                    VesselTooltip.IsOpen = true;
                }
            }
        }
        else
        {
            _lastHoveredMmsi = 0;
            VesselTooltip.IsOpen = false;
        }
    }

    private void OnMapMouseLeave(object sender, MouseEventArgs e)
    {
        _lastHoveredMmsi = 0;
        VesselTooltip.IsOpen = false;
    }
}
