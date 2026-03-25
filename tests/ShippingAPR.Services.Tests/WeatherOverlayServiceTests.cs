using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class WeatherOverlayServiceTests : IDisposable
{
    private readonly Mock<IMarineWeatherClient> _weatherClient = new();
    private readonly WeatherOverlayService _sut;

    public WeatherOverlayServiceTests()
    {
        _sut = new WeatherOverlayService(
            _weatherClient.Object,
            NullLogger<WeatherOverlayService>.Instance);
    }

    [Fact]
    public async Task Disabled_DoesNotFetchWeather()
    {
        _sut.IsEnabled = false;

        await _sut.RefreshAsync(50, 60, 10, 20);

        _weatherClient.Verify(
            w => w.GetWeatherGridAsync(It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Enabled_FetchesWeatherGrid()
    {
        _sut.IsEnabled = true;
        var grid = new MarineWeatherGrid
        {
            Points = [new MarineWeather { Latitude = 55, Longitude = 15 }],
            MinLat = 50, MaxLat = 60, MinLon = 10, MaxLon = 20,
            LatSteps = 6, LonSteps = 6
        };
        _weatherClient.Setup(w => w.GetWeatherGridAsync(50, 60, 10, 20, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grid);

        await _sut.RefreshAsync(50, 60, 10, 20);

        _sut.CurrentGrid.Should().NotBeNull();
        _sut.CurrentGrid!.Points.Should().HaveCount(1);
    }

    [Fact]
    public async Task WeatherUpdated_EventFired()
    {
        _sut.IsEnabled = true;
        var updated = false;
        _sut.WeatherUpdated += (_, _) => updated = true;

        _weatherClient.Setup(w => w.GetWeatherGridAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarineWeatherGrid
            {
                Points = [], MinLat = 50, MaxLat = 60, MinLon = 10, MaxLon = 20,
                LatSteps = 6, LonSteps = 6
            });

        await _sut.RefreshAsync(50, 60, 10, 20);

        updated.Should().BeTrue();
    }

    [Fact]
    public void Clear_NullsGridAndFiresEvent()
    {
        var updated = false;
        _sut.WeatherUpdated += (_, _) => updated = true;

        _sut.Clear();

        _sut.CurrentGrid.Should().BeNull();
        updated.Should().BeTrue();
    }

    [Fact]
    public async Task NullResponse_DoesNotUpdateGrid()
    {
        _sut.IsEnabled = true;
        _weatherClient.Setup(w => w.GetWeatherGridAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarineWeatherGrid?)null);

        await _sut.RefreshAsync(50, 60, 10, 20);

        _sut.CurrentGrid.Should().BeNull();
    }

    [Fact]
    public async Task ApiFailure_HandledGracefully()
    {
        _sut.IsEnabled = true;
        _weatherClient.Setup(w => w.GetWeatherGridAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API down"));

        await _sut.RefreshAsync(50, 60, 10, 20);

        _sut.CurrentGrid.Should().BeNull();
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
