using CommunityToolkit.Mvvm.ComponentModel;
using ShippingAPR.Core.Models;

namespace ShippingAPR.App.ViewModels;

public partial class VesselDetailViewModel : ObservableObject
{
    [ObservableProperty]
    private Vessel? _vessel;

    [ObservableProperty]
    private bool _hasVessel;

    partial void OnVesselChanged(Vessel? value)
    {
        HasVessel = value is not null;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(MmsiText));
        OnPropertyChanged(nameof(ImoText));
        OnPropertyChanged(nameof(CallSignText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(FlagText));
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(CourseText));
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(DestinationText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DimensionsText));
        OnPropertyChanged(nameof(DraughtText));
        OnPropertyChanged(nameof(EtaText));
        OnPropertyChanged(nameof(DistanceText));
        OnPropertyChanged(nameof(TimeToArrivalText));
        OnPropertyChanged(nameof(EffectiveSpeedText));
        OnPropertyChanged(nameof(CourseDeviationText));
        OnPropertyChanged(nameof(HasEta));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(LastUpdateText));
        OnPropertyChanged(nameof(TrackPointCount));
    }

    public string DisplayName => Vessel?.DisplayName ?? "--";
    public string MmsiText => Vessel?.Mmsi.ToString() ?? "--";
    public string ImoText => Vessel?.StaticData?.ImoNumber is > 0 ? Vessel.StaticData.ImoNumber.ToString() : "--";
    public string CallSignText => Vessel?.StaticData?.CallSign ?? "--";
    public string TypeText => Vessel?.Type.ToString() ?? "--";
    public string FlagText => Vessel?.StaticData?.CountryCode ?? "--";

    public string SpeedText =>
        Vessel?.CurrentPosition is not null
            ? $"{Vessel.CurrentPosition.SpeedOverGround:F1} kn"
            : "--";

    public string CourseText =>
        Vessel?.CurrentPosition is not null
            ? $"{Vessel.CurrentPosition.CourseOverGround:F0}°"
            : "--";

    public string HeadingText =>
        Vessel?.CurrentPosition is not null
            ? $"{Vessel.CurrentPosition.TrueHeading:F0}°"
            : "--";

    public string DestinationText =>
        Vessel?.StaticData?.Destination ?? Resources.Strings.UnknownDestination;

    public string StatusText =>
        Vessel?.CurrentPosition?.Status.ToString() ?? "--";

    public string DimensionsText =>
        Vessel?.StaticData is { LengthOverall: > 0 }
            ? $"{Vessel.StaticData.LengthOverall}m × {Vessel.StaticData.Beam}m"
            : "--";

    public string DraughtText =>
        Vessel?.StaticData is { Draught: > 0 }
            ? $"{Vessel.StaticData.Draught:F1}m"
            : "--";

    public string PositionText =>
        Vessel?.CurrentPosition is not null
            ? $"{Vessel.CurrentPosition.Latitude:F4}°N, {Vessel.CurrentPosition.Longitude:F4}°E"
            : "--";

    public string LastUpdateText =>
        Vessel is not null
            ? Vessel.LastUpdated.ToString("HH:mm:ss UTC")
            : "--";

    public int TrackPointCount => Vessel?.Track.Count ?? 0;

    // ETA properties
    public bool HasEta => Vessel?.CalculatedEta is not null;

    public string EtaText =>
        Vessel?.CalculatedEta is not null
            ? Vessel.CalculatedEta.EstimatedArrival.ToString("yyyy-MM-dd HH:mm UTC")
            : "--";

    public string DistanceText =>
        Vessel?.CalculatedEta is not null
            ? $"{Vessel.CalculatedEta.DistanceNauticalMiles:F1} NM"
            : "--";

    public string TimeToArrivalText
    {
        get
        {
            if (Vessel?.CalculatedEta is null) return "--";
            var ts = Vessel.CalculatedEta.TimeToArrival;
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m";
        }
    }

    public string EffectiveSpeedText =>
        Vessel?.CalculatedEta is not null
            ? $"{Vessel.CalculatedEta.EffectiveSpeedKnots:F1} kn"
            : "--";

    public string CourseDeviationText =>
        Vessel?.CalculatedEta is not null
            ? $"{Vessel.CalculatedEta.CourseDeviationDegrees:F1}°"
            : "--";
}
