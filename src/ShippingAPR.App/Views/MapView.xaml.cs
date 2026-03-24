using System.Windows.Controls;
using Mapsui.Projections;
using Mapsui.UI.Wpf;
using ShippingAPR.App.ViewModels;

namespace ShippingAPR.App.Views;

public partial class MapView : UserControl
{
    public MapView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MapViewModel vm)
        {
            MapControl.Map = vm.Map;

            MapControl.Info += (_, args) =>
            {
                if (args.MapInfo?.WorldPosition is not null)
                {
                    var lonLat = SphericalMercator.ToLonLat(
                        args.MapInfo.WorldPosition.X,
                        args.MapInfo.WorldPosition.Y);
                    vm.HandleMapClick(lonLat.lon, lonLat.lat);
                }

                // Check if a vessel feature was clicked
                if (args.MapInfo?.Feature?["MMSI"] is int mmsi
                    && System.Windows.Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
                {
                    mainVm.VesselListViewModel.SelectVesselByMmsi(mmsi);
                    vm.CenterOnVessel(mainVm.VesselListViewModel.SelectedVessel);
                }
            };

            // Wire viewport changes for dynamic area tracking
            vm.Map.Navigator.Navigated += (_, _) =>
            {
                vm.OnViewportChanged();
            };
        }
    }
}
