using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using ShippingAPR.App.Configuration;
using ShippingAPR.Core.Enums;

namespace ShippingAPR.App.ViewModels;

public partial class FilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _showCargo = true;

    [ObservableProperty]
    private bool _showTanker = true;

    [ObservableProperty]
    private bool _showPassenger = true;

    [ObservableProperty]
    private bool _showFishing = true;

    [ObservableProperty]
    private bool _showTugPilot = true;

    [ObservableProperty]
    private bool _showOther = true;

    [ObservableProperty]
    private double _minSpeed;

    [ObservableProperty]
    private double _maxSpeed;

    public FilterViewModel(IOptions<UiOptions> uiOptions)
    {
        _maxSpeed = uiOptions.Value.DefaultMaxSpeedFilter;
    }

    public bool ShouldShow(VesselType type, double speed)
    {
        if (speed < MinSpeed || speed > MaxSpeed)
            return false;

        return type switch
        {
            VesselType.Cargo => ShowCargo,
            VesselType.Tanker => ShowTanker,
            VesselType.Passenger => ShowPassenger,
            VesselType.Fishing => ShowFishing,
            VesselType.Tug or VesselType.Pilot => ShowTugPilot,
            _ => ShowOther
        };
    }
}
