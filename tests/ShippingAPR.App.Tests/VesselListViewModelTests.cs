using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ShippingAPR.App.Configuration;
using ShippingAPR.App.ViewModels;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.App.Tests;

public class VesselListViewModelTests
{
    private readonly VesselStore _store = new();
    private readonly VesselListViewModel _vm;

    public VesselListViewModelTests()
    {
        var filter = new FilterViewModel(Options.Create(new UiOptions { DefaultMaxSpeedFilter = 100 }));
        _vm = new VesselListViewModel(_store, Mock.Of<IWatchlistService>(), filter, Options.Create(new UiOptions()));
    }

    private void Add(int mmsi, string name, double speed)
    {
        _store.AddOrUpdate(mmsi,
            new VesselPosition
            {
                Latitude = 57.7, Longitude = 11.9,
                SpeedOverGround = speed, CourseOverGround = 0, TrueHeading = 0,
                Status = NavigationalStatus.UnderWayUsingEngine
            },
            new VesselStaticData { Name = name });
    }

    [Fact]
    public void SortBySpeed_OrdersBySpeedAscending()
    {
        Add(200000001, "Charlie", 15);
        Add(200000002, "Bravo", 5);
        Add(200000003, "Alpha", 10);

        _vm.SortBy = "Speed"; // change from default "Name" → triggers a re-sort

        _vm.Vessels.Should().HaveCount(3);
        _vm.Vessels.Select(v => v.Speed).Should().BeInAscendingOrder();
    }

    [Fact]
    public void SortByName_OrdersAlphabetically()
    {
        Add(200000001, "Charlie", 15);
        Add(200000002, "Bravo", 5);
        Add(200000003, "Alpha", 10);

        // Toggle through another value so the change to "Name" is observed.
        _vm.SortBy = "Speed";
        _vm.SortBy = "Name";

        _vm.Vessels.Select(v => v.Name).Should().ContainInOrder("Alpha", "Bravo", "Charlie");
    }
}
