using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Formatting;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class VesselDetailViewModel : ObservableObject, IDisposable
{
    private readonly IWatchlistService _watchlistService;
    private readonly VoyageNarrativeService _narrativeService;
    private readonly EmissionsEstimatorService _emissionsService;
    private readonly IVesselStore _vesselStore;
    private readonly EventHandler _onWatchlistChanged;
    private readonly EventHandler<Vessel> _onVesselUpdated;

    [ObservableProperty]
    private Vessel? _vessel;

    [ObservableProperty]
    private bool _hasVessel;

    [ObservableProperty]
    private bool _isWatched;

    [ObservableProperty]
    private string _voyageStoryText = "";

    public VesselDetailViewModel(IWatchlistService watchlistService, VoyageNarrativeService narrativeService, EmissionsEstimatorService emissionsService, IVesselStore vesselStore)
    {
        _watchlistService = watchlistService;
        _narrativeService = narrativeService;
        _emissionsService = emissionsService;
        _vesselStore = vesselStore;

        _onWatchlistChanged = (_, _) => UpdateIsWatched();
        _onVesselUpdated = OnStoreVesselUpdated;
        _watchlistService.WatchlistChanged += _onWatchlistChanged;
        // Refresh the panel live while a vessel stays selected (the Vessel POCO has no
        // INotifyPropertyChanged, so without this the panel froze until reselection).
        _vesselStore.VesselUpdated += _onVesselUpdated;
    }

    private void OnStoreVesselUpdated(object? sender, Vessel vessel)
    {
        if (Vessel is not null && vessel.Mmsi == Vessel.Mmsi)
            RaiseComputedProperties();
    }

    partial void OnVesselChanged(Vessel? value)
    {
        HasVessel = value is not null;
        UpdateIsWatched();
        RaiseComputedProperties();

        // Generate voyage story (only on selection change — it's relatively expensive)
        if (value is not null)
            VoyageStoryText = _narrativeService.GenerateNarrative(value.Mmsi);
        else
            VoyageStoryText = "";
    }

    private void RaiseComputedProperties()
    {
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
        OnPropertyChanged(nameof(EmissionsCo2Text));
        OnPropertyChanged(nameof(EmissionsFuelText));
        OnPropertyChanged(nameof(EmissionsSoxText));
        OnPropertyChanged(nameof(EmissionsNoxText));
        OnPropertyChanged(nameof(CiiRatingText));
        OnPropertyChanged(nameof(CiiRatingColor));
        OnPropertyChanged(nameof(HasEmissions));
    }

    public void Dispose()
    {
        _watchlistService.WatchlistChanged -= _onWatchlistChanged;
        _vesselStore.VesselUpdated -= _onVesselUpdated;
    }

    [RelayCommand]
    private void ToggleWatch()
    {
        if (Vessel is null) return;
        _watchlistService.Toggle(Vessel.Mmsi);
    }

    private void UpdateIsWatched()
    {
        IsWatched = Vessel is not null && _watchlistService.IsWatched(Vessel.Mmsi);
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
        Vessel?.StaticData?.Destination ?? "Unknown destination";

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
            ? CoordinateFormatter.Format(Vessel.CurrentPosition.Latitude, Vessel.CurrentPosition.Longitude)
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

    // Emissions properties
    private Core.Models.EmissionEstimate? CurrentEmissions =>
        Vessel is not null ? _emissionsService.Estimate(Vessel) : null;

    public bool HasEmissions => CurrentEmissions is not null;

    public string EmissionsCo2Text =>
        CurrentEmissions is not null
            ? $"{CurrentEmissions.Co2TonnesPerHour:F3} t/h"
            : "--";

    public string EmissionsFuelText =>
        CurrentEmissions is not null
            ? $"{CurrentEmissions.FuelTonnesPerHour:F3} t/h"
            : "--";

    public string EmissionsSoxText =>
        CurrentEmissions is not null
            ? $"{CurrentEmissions.SoxKgPerHour:F1} kg/h"
            : "--";

    public string EmissionsNoxText =>
        CurrentEmissions is not null
            ? $"{CurrentEmissions.NoxKgPerHour:F1} kg/h"
            : "--";

    public string CiiRatingText =>
        CurrentEmissions is not null
            ? CurrentEmissions.CiiRating.ToString()
            : "--";

    public string CiiRatingColor => CurrentEmissions?.CiiRating switch
    {
        'A' => "#34D399", // green
        'B' => "#60A5FA", // blue
        'C' => "#FBBF24", // yellow
        'D' => "#FB923C", // orange
        'E' => "#EF4444", // red
        _ => "#9E9E9E"    // gray
    };
}
