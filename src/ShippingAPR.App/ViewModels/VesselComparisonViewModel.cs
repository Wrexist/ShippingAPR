using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class VesselComparisonViewModel : ObservableObject
{
    private readonly EmissionsEstimatorService _emissionsService;

    [ObservableProperty]
    private Vessel? _vesselA;

    [ObservableProperty]
    private Vessel? _vesselB;

    [ObservableProperty]
    private bool _hasComparison;

    public VesselComparisonViewModel(EmissionsEstimatorService emissionsService)
    {
        _emissionsService = emissionsService;
    }

    public void SetVessels(Vessel? a, Vessel? b)
    {
        VesselA = a;
        VesselB = b;
        HasComparison = a is not null && b is not null;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void Clear()
    {
        VesselA = null;
        VesselB = null;
        HasComparison = false;
        OnPropertyChanged(string.Empty);
    }

    // ── Vessel A properties ──

    public string NameA => VesselA?.DisplayName ?? "—";
    public string TypeA => VesselA?.Type.ToString() ?? "—";
    public string FlagA => VesselA?.StaticData?.CountryCode ?? "—";
    public string SpeedA => VesselA?.CurrentPosition is { } pa ? $"{pa.SpeedOverGround:F1} kn" : "—";
    public string CourseA => VesselA?.CurrentPosition is { } ca ? $"{ca.CourseOverGround:F1}°" : "—";
    public string HeadingA => VesselA?.CurrentPosition is { } ha ? $"{ha.TrueHeading:F0}°" : "—";
    public string DestinationA => VesselA?.StaticData?.Destination ?? "—";
    public string DimensionsA => FormatDimensions(VesselA);
    public string DraughtA => VesselA?.StaticData?.Draught > 0 ? $"{VesselA.StaticData.Draught:F1} m" : "—";
    public string StatusA => VesselA?.CurrentPosition?.Status.ToString() ?? "—";
    public string EmissionsA => FormatEmissions(VesselA);

    // ── Vessel B properties ──

    public string NameB => VesselB?.DisplayName ?? "—";
    public string TypeB => VesselB?.Type.ToString() ?? "—";
    public string FlagB => VesselB?.StaticData?.CountryCode ?? "—";
    public string SpeedB => VesselB?.CurrentPosition is { } pb ? $"{pb.SpeedOverGround:F1} kn" : "—";
    public string CourseB => VesselB?.CurrentPosition is { } cb ? $"{cb.CourseOverGround:F1}°" : "—";
    public string HeadingB => VesselB?.CurrentPosition is { } hb ? $"{hb.TrueHeading:F0}°" : "—";
    public string DestinationB => VesselB?.StaticData?.Destination ?? "—";
    public string DimensionsB => FormatDimensions(VesselB);
    public string DraughtB => VesselB?.StaticData?.Draught > 0 ? $"{VesselB.StaticData.Draught:F1} m" : "—";
    public string StatusB => VesselB?.CurrentPosition?.Status.ToString() ?? "—";
    public string EmissionsB => FormatEmissions(VesselB);

    // ── Comparison deltas ──

    public string SpeedDelta
    {
        get
        {
            if (VesselA?.CurrentPosition is null || VesselB?.CurrentPosition is null) return "";
            var diff = VesselA.CurrentPosition.SpeedOverGround - VesselB.CurrentPosition.SpeedOverGround;
            return diff switch
            {
                > 0.1 => $"A is {diff:F1} kn faster",
                < -0.1 => $"B is {-diff:F1} kn faster",
                _ => "Same speed"
            };
        }
    }

    public string SizeDelta
    {
        get
        {
            var lenA = VesselA?.StaticData?.LengthOverAll ?? 0;
            var lenB = VesselB?.StaticData?.LengthOverAll ?? 0;
            if (lenA == 0 || lenB == 0) return "";
            var diff = lenA - lenB;
            return diff switch
            {
                > 0 => $"A is {diff} m longer",
                < 0 => $"B is {-diff} m longer",
                _ => "Same length"
            };
        }
    }

    private static string FormatDimensions(Vessel? v)
    {
        if (v?.StaticData is null) return "—";
        var len = v.StaticData.LengthOverAll;
        var beam = v.StaticData.Beam;
        return len > 0 && beam > 0 ? $"{len} × {beam} m" : "—";
    }

    private string FormatEmissions(Vessel? v)
    {
        if (v is null) return "—";
        var estimate = _emissionsService.Estimate(v);
        if (estimate is null) return "—";
        return $"{estimate.TonsPerHour:F2} t/hr (CII: {estimate.CiiRating})";
    }
}
