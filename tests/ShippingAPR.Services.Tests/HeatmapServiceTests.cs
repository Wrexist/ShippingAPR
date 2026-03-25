using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class HeatmapServiceTests : IDisposable
{
    private readonly VesselStore _store = new();
    private readonly HeatmapService _sut;

    public HeatmapServiceTests()
    {
        _sut = new HeatmapService(_store, NullLogger<HeatmapService>.Instance);
    }

    [Fact]
    public void Disabled_DoesNotAccumulateData()
    {
        _sut.IsEnabled = false;

        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.9), null);

        _sut.TotalDataPoints.Should().Be(0);
    }

    [Fact]
    public void Enabled_AccumulatesDataPoints()
    {
        _sut.IsEnabled = true;

        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.9), null);
        _store.AddOrUpdate(200000002, CreatePosition(57.8, 12.0), null);

        _sut.TotalDataPoints.Should().Be(2);
    }

    [Fact]
    public void Clear_ResetsAllData()
    {
        _sut.IsEnabled = true;
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.9), null);

        _sut.Clear();

        _sut.TotalDataPoints.Should().Be(0);
    }

    [Fact]
    public void GenerateGrid_ReturnsCorrectDimensions()
    {
        _sut.IsEnabled = true;
        _store.AddOrUpdate(200000001, CreatePosition(57.7, 11.9), null);

        var grid = _sut.GenerateGrid(57.0, 58.0, 11.0, 12.0, resolution: 10);

        grid.Resolution.Should().Be(10);
        grid.Cells.GetLength(0).Should().Be(10);
        grid.Cells.GetLength(1).Should().Be(10);
    }

    [Fact]
    public void GenerateGrid_ProjectsPointsOntoGrid()
    {
        _sut.IsEnabled = true;
        _store.AddOrUpdate(200000001, CreatePosition(57.5, 11.5), null);

        var grid = _sut.GenerateGrid(57.0, 58.0, 11.0, 12.0, resolution: 10);

        grid.MaxIntensity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void HeatmapUpdated_FiresEvery100Points()
    {
        _sut.IsEnabled = true;
        var updateCount = 0;
        _sut.HeatmapUpdated += (_, _) => updateCount++;

        for (int i = 0; i < 100; i++)
        {
            _store.AddOrUpdate(200000001 + i,
                CreatePosition(57.0 + i * 0.01, 11.0 + i * 0.01), null);
        }

        updateCount.Should().Be(1);
    }

    private static VesselPosition CreatePosition(double lat, double lon)
    {
        return new VesselPosition
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = 10,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = NavigationalStatus.UnderWayUsingEngine
        };
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
