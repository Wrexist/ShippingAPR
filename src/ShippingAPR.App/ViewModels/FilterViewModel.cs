using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using ShippingAPR.App.Configuration;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;

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

    [ObservableProperty]
    private string _destinationFilter = string.Empty;

    [ObservableProperty]
    private string _flagFilter = string.Empty;

    [ObservableProperty]
    private NavigationalStatus? _statusFilter;

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

    /// <summary>
    /// Extended filter that also checks destination, flag, and navigational status.
    /// </summary>
    public bool ShouldShowVessel(Vessel vessel)
    {
        var type = vessel.StaticData?.ShipType ?? VesselType.Unknown;
        var speed = vessel.CurrentPosition?.SpeedOverGround ?? 0;

        if (!ShouldShow(type, speed))
            return false;

        // Destination filter (case-insensitive partial match)
        if (!string.IsNullOrEmpty(DestinationFilter) &&
            (vessel.StaticData?.Destination is null ||
             !vessel.StaticData.Destination.Contains(DestinationFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Flag/country filter (case-insensitive partial match)
        if (!string.IsNullOrEmpty(FlagFilter) &&
            (vessel.StaticData?.CountryCode is null ||
             !vessel.StaticData.CountryCode.Contains(FlagFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Navigational status filter
        if (StatusFilter.HasValue &&
            vessel.CurrentPosition?.Status != StatusFilter.Value)
            return false;

        return true;
    }
}
