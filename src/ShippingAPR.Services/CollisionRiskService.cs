using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class CollisionRiskService : BackgroundService
{
    private readonly IVesselStore _vesselStore;
    private readonly NotificationService _notificationService;
    private readonly ILogger<CollisionRiskService> _logger;
    private readonly TrackingOptions _options;

    // Track active risk episodes to avoid duplicate alerts (key: sorted MMSI pair,
    // value: time the pair was last seen at risk).
    private readonly ConcurrentDictionary<(int, int), DateTime> _activeRisks = new();

    // Overridable for deterministic testing.
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    public CollisionRiskService(
        IVesselStore vesselStore,
        NotificationService notificationService,
        ILogger<CollisionRiskService> logger,
        IOptions<TrackingOptions> options)
    {
        _vesselStore = vesselStore;
        _notificationService = notificationService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for vessels to start arriving
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ScanForCollisionRisks();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during collision risk scan");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CpaScanIntervalSeconds), stoppingToken);
        }
    }

    internal void ScanForCollisionRisks()
    {
        var vessels = _vesselStore.Vessels.Values
            .Where(v => v.CurrentPosition is not null &&
                        v.CurrentPosition.SpeedOverGround >= 0.5)
            .ToList();

        if (vessels.Count < 2) return;

        var maxTcpa = TimeSpan.FromMinutes(_options.TcpaWindowMinutes);
        var preFilterNm = _options.CpaPreFilterDistanceNm;
        var warningNm = _options.CpaWarningDistanceNm;

        var now = Clock.GetUtcNow().UtcDateTime;

        // A risk episode stays active while the pair keeps being seen at risk.
        // Once a pair hasn't been seen at risk for the clear window, the episode
        // is considered resolved, so a later re-detection is a new episode that
        // alerts again. This replaces the previous fixed-cadence re-alert bug.
        var clearWindow = TimeSpan.FromSeconds(Math.Max(60, _options.CpaScanIntervalSeconds * 3));
        foreach (var kv in _activeRisks)
        {
            if (now - kv.Value > clearWindow)
                _activeRisks.TryRemove(kv.Key, out _);
        }

        for (int i = 0; i < vessels.Count; i++)
        {
            for (int j = i + 1; j < vessels.Count; j++)
            {
                var v1 = vessels[i];
                var v2 = vessels[j];
                var p1 = v1.CurrentPosition!;
                var p2 = v2.CurrentPosition!;

                // Quick distance pre-filter using Haversine
                var dist = HaversineCalculator.DistanceInNauticalMiles(
                    p1.Latitude, p1.Longitude, p2.Latitude, p2.Longitude);
                if (dist > preFilterNm) continue;

                // Calculate CPA
                var result = CpaCalculator.Calculate(
                    v1.Mmsi, p1.Latitude, p1.Longitude, p1.CourseOverGround, p1.SpeedOverGround,
                    v2.Mmsi, p2.Latitude, p2.Longitude, p2.CourseOverGround, p2.SpeedOverGround);

                if (result is null) continue;
                if (result.CpaNauticalMiles > warningNm) continue;
                if (result.TimeToCpa > maxTcpa) continue;

                // Create sorted pair key to avoid duplicate alerts
                var key = v1.Mmsi < v2.Mmsi ? (v1.Mmsi, v2.Mmsi) : (v2.Mmsi, v1.Mmsi);

                // Refresh the last-seen-at-risk time every scan; only alert on the
                // first detection of an episode.
                var alreadyActive = _activeRisks.ContainsKey(key);
                _activeRisks[key] = now;
                if (alreadyActive) continue;

                _logger.LogWarning(
                    "Collision risk: {V1} and {V2}, CPA={Cpa:F2} NM in {Tcpa}",
                    v1.DisplayName, v2.DisplayName,
                    result.CpaNauticalMiles, result.TimeToCpa);

                // Show seconds for sub-minute TCPA so the most urgent case
                // doesn't read as "0m".
                var tcpa = result.TimeToCpa;
                var tcpaText = tcpa.TotalMinutes >= 1
                    ? $"{(int)tcpa.TotalMinutes}m"
                    : $"{(int)tcpa.TotalSeconds}s";

                _notificationService.Publish(new NotificationMessage
                {
                    Title = "Collision Risk Detected",
                    Body = $"{v1.DisplayName} & {v2.DisplayName}: " +
                           $"CPA {result.CpaNauticalMiles:F2} NM in {tcpaText}",
                    Type = NotificationType.CollisionRisk
                });
            }
        }
    }
}
