using System.Windows.Controls;
using Mapsui.UI;
using Mapsui.Projections;
using ShippingAPR.App.ViewModels;

namespace ShippingAPR.App.Views;

public partial class MapView : UserControl
{
    private MapViewModel? _currentViewModel;
    private EventHandler<MapInfoEventArgs>? _infoHandler;
    private EventHandler? _navigatedHandler;

    public MapView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe previous handlers to prevent leaks and double-registration
        if (_currentViewModel is not null)
        {
            if (_infoHandler is not null)
                MapControl.Info -= _infoHandler;
            if (_navigatedHandler is not null)
                _currentViewModel.Map.Navigator.Navigated -= _navigatedHandler;
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
        }
        else
        {
            _currentViewModel = null;
            _infoHandler = null;
            _navigatedHandler = null;
        }
    }
}
