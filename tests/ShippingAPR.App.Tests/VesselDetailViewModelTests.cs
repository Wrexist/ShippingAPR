using FluentAssertions;
using ShippingAPR.App.ViewModels;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.App.Tests;

public class VesselDetailViewModelTests
{
    private readonly VesselDetailViewModel _vm = new();

    [Fact]
    public void NullVessel_AllPropertiesReturnDefaults()
    {
        _vm.Vessel = null;

        _vm.HasVessel.Should().BeFalse();
        _vm.DisplayName.Should().Be("--");
        _vm.MmsiText.Should().Be("--");
        _vm.ImoText.Should().Be("--");
        _vm.CallSignText.Should().Be("--");
        _vm.TypeText.Should().Be("--");
        _vm.FlagText.Should().Be("--");
        _vm.SpeedText.Should().Be("--");
        _vm.CourseText.Should().Be("--");
        _vm.HeadingText.Should().Be("--");
        _vm.StatusText.Should().Be("--");
        _vm.DimensionsText.Should().Be("--");
        _vm.DraughtText.Should().Be("--");
        _vm.PositionText.Should().Be("--");
        _vm.LastUpdateText.Should().Be("--");
        _vm.TrackPointCount.Should().Be(0);
        _vm.HasEta.Should().BeFalse();
        _vm.EtaText.Should().Be("--");
        _vm.DistanceText.Should().Be("--");
        _vm.TimeToArrivalText.Should().Be("--");
        _vm.EffectiveSpeedText.Should().Be("--");
        _vm.CourseDeviationText.Should().Be("--");
    }

    [Fact]
    public void HasVessel_TrueWhenVesselSet()
    {
        _vm.Vessel = new Vessel { Mmsi = 123456789 };
        _vm.HasVessel.Should().BeTrue();
    }

    [Fact]
    public void HasVessel_FalseWhenVesselCleared()
    {
        _vm.Vessel = new Vessel { Mmsi = 123456789 };
        _vm.Vessel = null;
        _vm.HasVessel.Should().BeFalse();
    }

    [Fact]
    public void DestinationText_NoDestination_ShowsUnknown()
    {
        _vm.Vessel = new Vessel { Mmsi = 123456789 };
        _vm.DestinationText.Should().Be("Unknown destination");
    }

    [Fact]
    public void DestinationText_WithDestination_ShowsDestination()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData { Destination = "SEGOT" });
        _vm.Vessel = vessel;
        _vm.DestinationText.Should().Be("SEGOT");
    }

    [Fact]
    public void SpeedText_WithPosition_FormattedCorrectly()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdatePosition(new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
            SpeedOverGround = 12.5,
            CourseOverGround = 45.3,
            TrueHeading = 43
        });
        _vm.Vessel = vessel;

        _vm.SpeedText.Should().Be("12.5 kn");
        _vm.CourseText.Should().Be("45°");
        _vm.HeadingText.Should().Be("43°");
    }

    [Fact]
    public void PositionText_FormattedWithFourDecimals()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdatePosition(new VesselPosition
        {
            Latitude = 57.7089,
            Longitude = 11.9746
        });
        _vm.Vessel = vessel;

        _vm.PositionText.Should().Be("57.7089°N, 11.9746°E");
    }

    [Fact]
    public void DimensionsText_WithDimensions_ShowsFormatted()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData
        {
            DimensionA = 140,
            DimensionB = 48,
            DimensionC = 15,
            DimensionD = 15
        });
        _vm.Vessel = vessel;

        _vm.DimensionsText.Should().Be("188m × 30m");
    }

    [Fact]
    public void DimensionsText_ZeroDimensions_ReturnsDash()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData());
        _vm.Vessel = vessel;

        _vm.DimensionsText.Should().Be("--");
    }

    [Fact]
    public void DraughtText_WithDraught_ShowsFormatted()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData { Draught = 6.1 });
        _vm.Vessel = vessel;

        _vm.DraughtText.Should().Be("6.1m");
    }

    [Fact]
    public void DraughtText_ZeroDraught_ReturnsDash()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData { Draught = 0 });
        _vm.Vessel = vessel;

        _vm.DraughtText.Should().Be("--");
    }

    [Fact]
    public void ImoText_ValidImo_ShowsNumber()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData { ImoNumber = 9145176 });
        _vm.Vessel = vessel;

        _vm.ImoText.Should().Be("9145176");
    }

    [Fact]
    public void ImoText_ZeroImo_ReturnsDash()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.UpdateStaticData(new VesselStaticData { ImoNumber = 0 });
        _vm.Vessel = vessel;

        _vm.ImoText.Should().Be("--");
    }

    [Fact]
    public void EtaProperties_WithEta_ShowFormatted()
    {
        var vessel = new Vessel { Mmsi = 123456789 };
        vessel.CalculatedEta = new EtaResult(
            DistanceNauticalMiles: 210.5,
            TimeToArrival: TimeSpan.FromHours(14),
            EstimatedArrival: new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc),
            CourseDeviationDegrees: 5.3,
            EffectiveSpeedKnots: 15.0);
        _vm.Vessel = vessel;

        _vm.HasEta.Should().BeTrue();
        _vm.EtaText.Should().Be("2026-06-15 14:00 UTC");
        _vm.DistanceText.Should().Be("210.5 NM");
        _vm.EffectiveSpeedText.Should().Be("15.0 kn");
        _vm.CourseDeviationText.Should().Be("5.3°");
    }

    [Fact]
    public void TimeToArrivalText_DaysHoursMinutes()
    {
        var vessel = new Vessel { Mmsi = 1 };
        vessel.CalculatedEta = new EtaResult(100, TimeSpan.FromHours(26.5),
            DateTime.UtcNow.AddHours(26.5), 0, 10);
        _vm.Vessel = vessel;

        _vm.TimeToArrivalText.Should().Be("1d 2h 30m");
    }

    [Fact]
    public void TimeToArrivalText_HoursMinutes()
    {
        var vessel = new Vessel { Mmsi = 1 };
        vessel.CalculatedEta = new EtaResult(50, TimeSpan.FromHours(5.75),
            DateTime.UtcNow.AddHours(5.75), 0, 10);
        _vm.Vessel = vessel;

        _vm.TimeToArrivalText.Should().Be("5h 45m");
    }

    [Fact]
    public void TimeToArrivalText_MinutesOnly()
    {
        var vessel = new Vessel { Mmsi = 1 };
        vessel.CalculatedEta = new EtaResult(5, TimeSpan.FromMinutes(30),
            DateTime.UtcNow.AddMinutes(30), 0, 10);
        _vm.Vessel = vessel;

        _vm.TimeToArrivalText.Should().Be("30m");
    }

    [Fact]
    public void PropertyChanged_FiresWhenVesselSet()
    {
        var changedProperties = new List<string>();
        _vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        _vm.Vessel = new Vessel { Mmsi = 123456789 };

        changedProperties.Should().Contain("Vessel");
        changedProperties.Should().Contain("HasVessel");
        changedProperties.Should().Contain("DisplayName");
        changedProperties.Should().Contain("SpeedText");
    }
}
