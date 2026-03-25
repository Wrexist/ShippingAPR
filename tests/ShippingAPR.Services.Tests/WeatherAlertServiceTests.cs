using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class WeatherAlertServiceTests : IDisposable
{
    private readonly VesselStore _store = new();
    private readonly Mock<IMarineWeatherClient> _weatherClient = new();
    private readonly NotificationService _notificationService;
    private readonly WeatherAlertService _sut;

    public WeatherAlertServiceTests()
    {
        var areaMonitor = new AreaMonitorService(_store, NullLogger<AreaMonitorService>.Instance);
        _notificationService = new NotificationService(
            areaMonitor,
            NullLogger<NotificationService>.Instance,
            Options.Create(new TrackingOptions()));
        _sut = new WeatherAlertService(
            _store,
            _weatherClient.Object,
            _notificationService,
            NullLogger<WeatherAlertService>.Instance);
    }

    [Fact]
    public async Task NoVessels_NoAlerts()
    {
        await _sut.CheckVesselWeatherAsync();

        _sut.ActiveAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task GaleConditions_IssuesAlert()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), null);

        _weatherClient.Setup(w => w.GetWeatherAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarineWeather
            {
                BeaufortScale = 9,
                WaveHeightMeters = 5.0,
                WindSpeedKnots = 45,
                VisibilityKm = 10
            });

        await _sut.CheckVesselWeatherAsync();

        _sut.ActiveAlerts.Should().NotBeEmpty();
        _notificationService.History.Should().Contain(n => n.Type == NotificationType.WeatherWarning);
    }

    [Fact]
    public async Task StormConditions_IssuesWarning()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), null);

        _weatherClient.Setup(w => w.GetWeatherAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarineWeather
            {
                BeaufortScale = 11,
                WaveHeightMeters = 8.0,
                WindSpeedKnots = 60,
                VisibilityKm = 5
            });

        await _sut.CheckVesselWeatherAsync();

        var alert = _sut.ActiveAlerts.First();
        alert.Type.Should().Be(WeatherAlertType.Storm);
        alert.Severity.Should().Be(WeatherSeverity.Warning);
    }

    [Fact]
    public async Task FogConditions_IssuesAdvisory()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), null);

        _weatherClient.Setup(w => w.GetWeatherAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarineWeather
            {
                BeaufortScale = 3,
                WaveHeightMeters = 0.5,
                WindSpeedKnots = 10,
                VisibilityKm = 0.5
            });

        await _sut.CheckVesselWeatherAsync();

        _sut.ActiveAlerts.Should().Contain(a => a.Type == WeatherAlertType.Fog);
    }

    [Fact]
    public async Task CalmWeather_NoAlerts()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), null);

        _weatherClient.Setup(w => w.GetWeatherAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarineWeather
            {
                BeaufortScale = 3,
                WaveHeightMeters = 0.5,
                WindSpeedKnots = 10,
                VisibilityKm = 10
            });

        await _sut.CheckVesselWeatherAsync();

        _sut.ActiveAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task AlertIssued_EventFired()
    {
        WeatherAlert? issuedAlert = null;
        _sut.AlertIssued += (_, a) => issuedAlert = a;

        _store.AddOrUpdate(200000001, CreatePosition(), null);
        _weatherClient.Setup(w => w.GetWeatherAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarineWeather
            {
                BeaufortScale = 9,
                WaveHeightMeters = 5.0,
                WindSpeedKnots = 45,
                VisibilityKm = 10
            });

        await _sut.CheckVesselWeatherAsync();

        issuedAlert.Should().NotBeNull();
    }

    [Fact]
    public async Task Cooldown_PreventsRepeatedAlerts()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), null);
        _weatherClient.Setup(w => w.GetWeatherAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarineWeather
            {
                BeaufortScale = 9,
                WaveHeightMeters = 5.0,
                WindSpeedKnots = 45,
                VisibilityKm = 10
            });

        await _sut.CheckVesselWeatherAsync();
        var firstCount = _notificationService.History.Count;

        await _sut.CheckVesselWeatherAsync();
        var secondCount = _notificationService.History.Count;

        secondCount.Should().Be(firstCount, "cooldown should prevent second alert within 15 minutes");
    }

    [Fact]
    public async Task NullWeatherResponse_HandledGracefully()
    {
        _store.AddOrUpdate(200000001, CreatePosition(), null);
        _weatherClient.Setup(w => w.GetWeatherAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarineWeather?)null);

        await _sut.CheckVesselWeatherAsync();

        _sut.ActiveAlerts.Should().BeEmpty();
    }

    private static VesselPosition CreatePosition()
    {
        return new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
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
