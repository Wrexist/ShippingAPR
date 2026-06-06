using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ShippingAPR.App.Configuration;
using ShippingAPR.App.ViewModels;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.App.Tests;

public class SearchViewModelTests
{
    private readonly Mock<IVesselStore> _storeMock;
    private readonly SearchViewModel _vm;

    public SearchViewModelTests()
    {
        _storeMock = new Mock<IVesselStore>();
        _vm = new SearchViewModel(
            _storeMock.Object,
            Options.Create(new UiOptions { SearchMaxResults = 5 }));
    }

    [Fact]
    public void EmptyQuery_ClearsResultsAndHides()
    {
        _vm.SearchQuery = "";

        _vm.Results.Should().BeEmpty();
        _vm.ShowResults.Should().BeFalse();
    }

    [Fact]
    public void WhitespaceQuery_ClearsResultsAndHides()
    {
        _vm.SearchQuery = "   ";

        _vm.Results.Should().BeEmpty();
        _vm.ShowResults.Should().BeFalse();
    }

    [Fact]
    public void ValidQuery_DelegatesToStoreSearch()
    {
        var vessel = new Vessel { Mmsi = 265000001 };
        vessel.UpdateStaticData(new VesselStaticData { Name = "TEST SHIP" });

        _storeMock.Setup(s => s.Search("TEST", It.IsAny<int>()))
            .Returns(new[] { vessel });

        _vm.SearchQuery = "TEST";

        _vm.Results.Should().HaveCount(1);
        _vm.Results[0].Mmsi.Should().Be(265000001);
        _vm.ShowResults.Should().BeTrue();
    }

    [Fact]
    public void Results_CappedAtMaxResults()
    {
        var vessels = Enumerable.Range(1, 10)
            .Select(i => new Vessel { Mmsi = i })
            .ToArray();

        _storeMock.Setup(s => s.Search("SHIP", It.IsAny<int>()))
            .Returns(vessels);

        _vm.SearchQuery = "SHIP";

        _vm.Results.Should().HaveCount(5); // max is 5
    }

    [Fact]
    public void NoResults_ShowsNoResultsMessage()
    {
        _storeMock.Setup(s => s.Search("NONEXISTENT", It.IsAny<int>()))
            .Returns(Enumerable.Empty<Vessel>());

        _vm.SearchQuery = "NONEXISTENT";

        _vm.Results.Should().BeEmpty();
        // The dropdown stays open to show a "no vessels found" message.
        _vm.HasNoResults.Should().BeTrue();
        _vm.ShowResults.Should().BeTrue();
    }

    [Fact]
    public void SelectResult_FiresVesselSelectedEvent()
    {
        var vessel = new Vessel { Mmsi = 265000001 };
        vessel.UpdateStaticData(new VesselStaticData { Name = "STENA" });

        Vessel? selectedVessel = null;
        _vm.VesselSelected += (_, v) => selectedVessel = v;

        _vm.SelectResultCommand.Execute(vessel);

        selectedVessel.Should().Be(vessel);
    }

    [Fact]
    public void SelectResult_SetsQueryToDisplayName()
    {
        var vessel = new Vessel { Mmsi = 265000001 };
        vessel.UpdateStaticData(new VesselStaticData { Name = "STENA GERMANICA" });

        // Need to stub the search that will be triggered by setting SearchQuery
        _storeMock.Setup(s => s.Search(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<Vessel>());

        _vm.SelectResultCommand.Execute(vessel);

        _vm.SearchQuery.Should().Be("STENA GERMANICA");
        _vm.ShowResults.Should().BeFalse();
    }

    [Fact]
    public void SelectResult_NullVessel_NoOp()
    {
        Vessel? selectedVessel = null;
        _vm.VesselSelected += (_, v) => selectedVessel = v;

        _vm.SelectResultCommand.Execute(null);

        selectedVessel.Should().BeNull();
    }

    [Fact]
    public void ClearSearch_ResetsEverything()
    {
        _storeMock.Setup(s => s.Search(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new[] { new Vessel { Mmsi = 1 } });

        _vm.SearchQuery = "TEST";
        _vm.Results.Should().NotBeEmpty();

        _vm.ClearSearchCommand.Execute(null);

        _vm.SearchQuery.Should().BeEmpty();
        _vm.Results.Should().BeEmpty();
        _vm.ShowResults.Should().BeFalse();
    }
}
