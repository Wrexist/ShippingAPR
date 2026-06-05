using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Monitors weather conditions and generates alerts when vessels
/// are in or approaching severe weather zones. Runs as a hosted background
/// service that samples vessel positions on a periodic timer.
/// </summary>
public sealed class WeatherAlertService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    private readonly IVesselStore _vesselStore;
    private readonly IMarineWeatherClient _weatherClient;
    private readonly NotificationService _notificationService;
    private readonly ILogger<WeatherAlertService> _logger;

    private readonly ConcurrentDictionary<string, WeatherAlert> _activeAlerts = new();
    private readonly ConcurrentDictionary<int, DateTime> _alertedVessels = new();
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(15);

    public IReadOnlyCollection<WeatherAlert> ActiveAlerts => _activeAlerts.Values.ToList();

    public event EventHandler<WeatherAlert>? AlertIssued;
    public event EventHandler<string>? AlertExpired;

    public WeatherAlertService(
        IVesselStore vesselStore,
        IMarineWeatherClient weatherClient,
        NotificationService notificationService,
        ILogger<WeatherAlertService> logger)
    {
        _vesselStore = vesselStore;
        _weatherClient = weatherClient;
        _notificationService = notificationService;
        _logger = logger;

        // Prevent unbounded growth of per-MMSI alert state over a 24/7 session.
        _vesselStore.VesselRemoved += (_, v) => _alertedVessels.TryRemove(v.Mmsi, out _);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(ScanInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckVesselWeatherAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during weather alert scan");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Check weather conditions for all tracked vessels and generate alerts.
    /// Called periodically from a background timer.
    /// </summary>
    public async Task CheckVesselWeatherAsync(CancellationToken ct = default)
    {
        var vessels = _vesselStore.Vessels.Values
            .Where(v => v.CurrentPosition is not null)
            .ToList();

        // Sample up to 20 vessel positions to avoid API overload.
        // Prioritize faster vessels (more likely in open water and at risk)
        // and those not recently checked.
        var sampled = vessels.Count > 20
            ? vessels
                .OrderByDescending(v => !_alertedVessels.ContainsKey(v.Mmsi) ? 1 : 0) // unchecked first
                .ThenByDescending(v => v.CurrentPosition!.SpeedOverGround) // faster vessels next
                .Take(20).ToList()
            : vessels;

        foreach (var vessel in sampled)
        {
            if (ct.IsCancellationRequested) break;

            var pos = vessel.CurrentPosition!;
            var weather = await _weatherClient.GetWeatherAsync(pos.Latitude, pos.Longitude, ct);
            if (weather is null) continue;

            // Check for severe conditions
            if (weather.BeaufortScale >= 8 || weather.WaveHeightMeters >= 4.0)
            {
                var alertType = weather.BeaufortScale >= 10 ? WeatherAlertType.Storm
                    : weather.BeaufortScale >= 8 ? WeatherAlertType.Gale
                    : WeatherAlertType.HighWaves;

                var severity = weather.BeaufortScale >= 10 ? WeatherSeverity.Warning
                    : weather.BeaufortScale >= 8 ? WeatherSeverity.Watch
                    : WeatherSeverity.Advisory;

                if (ShouldAlert(vessel.Mmsi))
                {
                    var alert = new WeatherAlert
                    {
                        Id = $"wx-{vessel.Mmsi}-{DateTime.UtcNow.Ticks}",
                        Type = alertType,
                        Severity = severity,
                        Description = $"Beaufort {weather.BeaufortScale}, waves {weather.WaveHeightMeters:F1}m, wind {weather.WindSpeedKnots:F0}kn",
                        AffectedArea = new BoundingBox(
                            pos.Latitude - 0.5, pos.Longitude - 0.5,
                            pos.Latitude + 0.5, pos.Longitude + 0.5),
                        WindSpeedKnots = weather.WindSpeedKnots,
                        WaveHeightMeters = weather.WaveHeightMeters,
                        ExpiresAt = DateTime.UtcNow.AddHours(3)
                    };

                    _activeAlerts[alert.Id] = alert;
                    _alertedVessels[vessel.Mmsi] = DateTime.UtcNow;

                    _notificationService.Publish(new NotificationMessage
                    {
                        Title = $"Weather {severity}: {alertType}",
                        Body = $"{vessel.DisplayName} in {alert.Description}",
                        Type = NotificationType.WeatherWarning
                    });

                    AlertIssued?.Invoke(this, alert);
                    _logger.LogInformation("Weather alert for {Vessel}: {Description}", vessel.DisplayName, alert.Description);
                }
            }

            // Check for fog
            if (weather.VisibilityKm < 1.0 && ShouldAlert(vessel.Mmsi))
            {
                var alert = new WeatherAlert
                {
                    Id = $"fog-{vessel.Mmsi}-{DateTime.UtcNow.Ticks}",
                    Type = WeatherAlertType.Fog,
                    Severity = WeatherSeverity.Advisory,
                    Description = $"Visibility {weather.VisibilityKm:F1}km",
                    AffectedArea = new BoundingBox(
                        pos.Latitude - 0.2, pos.Longitude - 0.2,
                        pos.Latitude + 0.2, pos.Longitude + 0.2),
                    ExpiresAt = DateTime.UtcNow.AddHours(2)
                };

                _activeAlerts[alert.Id] = alert;
                _alertedVessels[vessel.Mmsi] = DateTime.UtcNow;

                _notificationService.Publish(new NotificationMessage
                {
                    Title = "Fog Advisory",
                    Body = $"{vessel.DisplayName}: {alert.Description}",
                    Type = NotificationType.WeatherWarning
                });

                AlertIssued?.Invoke(this, alert);
            }
        }

        // Purge expired alerts
        var expired = _activeAlerts.Where(a => a.Value.ExpiresAt < DateTime.UtcNow).ToList();
        foreach (var kvp in expired)
        {
            _activeAlerts.TryRemove(kvp.Key, out _);
            AlertExpired?.Invoke(this, kvp.Key);
        }
    }

    private bool ShouldAlert(int mmsi)
    {
        if (_alertedVessels.TryGetValue(mmsi, out var lastAlert))
            return DateTime.UtcNow - lastAlert > AlertCooldown;
        return true;
    }
}
