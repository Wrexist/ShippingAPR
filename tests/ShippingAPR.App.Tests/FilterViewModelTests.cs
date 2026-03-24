using FluentAssertions;
using Microsoft.Extensions.Options;
using ShippingAPR.App.Configuration;
using ShippingAPR.App.ViewModels;
using ShippingAPR.Core.Enums;
using Xunit;

namespace ShippingAPR.App.Tests;

public class FilterViewModelTests
{
    private readonly FilterViewModel _vm;

    public FilterViewModelTests()
    {
        _vm = new FilterViewModel(Options.Create(new UiOptions { DefaultMaxSpeedFilter = 50 }));
    }

    [Fact]
    public void DefaultState_AllTypesShown()
    {
        _vm.ShowCargo.Should().BeTrue();
        _vm.ShowTanker.Should().BeTrue();
        _vm.ShowPassenger.Should().BeTrue();
        _vm.ShowFishing.Should().BeTrue();
        _vm.ShowTugPilot.Should().BeTrue();
        _vm.ShowOther.Should().BeTrue();
    }

    [Fact]
    public void DefaultState_SpeedRange()
    {
        _vm.MinSpeed.Should().Be(0);
        _vm.MaxSpeed.Should().Be(50);
    }

    [Theory]
    [InlineData(VesselType.Cargo)]
    [InlineData(VesselType.Tanker)]
    [InlineData(VesselType.Passenger)]
    [InlineData(VesselType.Fishing)]
    [InlineData(VesselType.Tug)]
    [InlineData(VesselType.Pilot)]
    [InlineData(VesselType.Unknown)]
    [InlineData(VesselType.Sailing)]
    public void ShouldShow_DefaultState_AllTypesVisible(VesselType type)
    {
        _vm.ShouldShow(type, 10).Should().BeTrue();
    }

    [Fact]
    public void ShouldShow_CargoDisabled_HidesCargo()
    {
        _vm.ShowCargo = false;
        _vm.ShouldShow(VesselType.Cargo, 10).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_TankerDisabled_HidesTanker()
    {
        _vm.ShowTanker = false;
        _vm.ShouldShow(VesselType.Tanker, 10).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_PassengerDisabled_HidesPassenger()
    {
        _vm.ShowPassenger = false;
        _vm.ShouldShow(VesselType.Passenger, 10).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_FishingDisabled_HidesFishing()
    {
        _vm.ShowFishing = false;
        _vm.ShouldShow(VesselType.Fishing, 10).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_TugPilotDisabled_HidesBothTugAndPilot()
    {
        _vm.ShowTugPilot = false;
        _vm.ShouldShow(VesselType.Tug, 10).Should().BeFalse();
        _vm.ShouldShow(VesselType.Pilot, 10).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_OtherDisabled_HidesUnknownTypes()
    {
        _vm.ShowOther = false;
        _vm.ShouldShow(VesselType.Unknown, 10).Should().BeFalse();
        _vm.ShouldShow(VesselType.Sailing, 10).Should().BeFalse();
        _vm.ShouldShow(VesselType.Military, 10).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_SpeedBelowMin_ReturnsFalse()
    {
        _vm.MinSpeed = 5;
        _vm.ShouldShow(VesselType.Cargo, 3).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_SpeedAboveMax_ReturnsFalse()
    {
        _vm.MaxSpeed = 20;
        _vm.ShouldShow(VesselType.Cargo, 25).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_SpeedAtMinBoundary_ReturnsTrue()
    {
        _vm.MinSpeed = 5;
        _vm.ShouldShow(VesselType.Cargo, 5).Should().BeTrue();
    }

    [Fact]
    public void ShouldShow_SpeedAtMaxBoundary_ReturnsTrue()
    {
        _vm.MaxSpeed = 20;
        _vm.ShouldShow(VesselType.Cargo, 20).Should().BeTrue();
    }

    [Fact]
    public void ShouldShow_CombinedTypeAndSpeed_BothApply()
    {
        _vm.ShowCargo = false;
        _vm.MinSpeed = 5;

        // Type filtered out
        _vm.ShouldShow(VesselType.Cargo, 10).Should().BeFalse();
        // Speed filtered out
        _vm.ShouldShow(VesselType.Tanker, 3).Should().BeFalse();
        // Both pass
        _vm.ShouldShow(VesselType.Tanker, 10).Should().BeTrue();
    }
}
