using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.Ports;

namespace ShippingAPR.Services;

public sealed class VesselTrackingService : BackgroundService, IVesselTrackingService
{
    private readonly IAisStreamClient _aisClient;
    private readonly IVesselStore _vesselStore;
    private readonly PortRepository _portRepository;
    private readonly ILogger<VesselTrackingService> _logger;
    private readonly TrackingOptions _trackingOptions;
    private readonly TimeSpan _purgeInterval;
    private readonly TimeSpan _staleAge;

    // ETA calculation caching — only recalculate when vessel moves significantly
    private readonly ConcurrentDictionary<int, (double Lat, double Lon, double Heading)> _lastEtaPosition = new();

    // Port resolution caching — avoid O(n) port scan on every message
    private readonly ConcurrentDictionary<string, Port?> _portCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _areaLock = new();
    private BoundingBox? _currentArea;
    private TaskCompletionSource<BoundingBox>? _areaWaiter;

    public ConnectionStatus ConnectionStatus => _aisClient.Status;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;

    public VesselTrackingService(
        IAisStreamClient aisClient,
        IVesselStore vesselStore,
        PortRepository portRepository,
        ILogger<VesselTrackingService> logger,
        IOptions<TrackingOptions> trackingOptions)
    {
        _aisClient = aisClient;
        _vesselStore = vesselStore;
        _portRepository = portRepository;
        _logger = logger;
        _trackingOptions = trackingOptions.Value;
        _purgeInterval = TimeSpan.FromMinutes(_trackingOptions.PurgeIntervalMinutes);
        _staleAge = TimeSpan.FromMinutes(_trackingOptions.StaleAgeMinutes);

        _aisClient.ConnectionStatusChanged += (_, status) =>
            ConnectionStatusChanged?.Invoke(this, status);

        _aisClient.MessageReceived += OnMessageReceived;

        _vesselStore.VesselRemoved += OnVesselRemoved;
    }

    public Task StartTrackingAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        lock (_areaLock)
        {
            _currentArea = area;
        }
        _areaWaiter?.TrySetResult(area);
        return _aisClient.ConnectAsync(area, cancellationToken);
    }

    public async Task StopTrackingAsync()
    {
        await _aisClient.DisconnectAsync();
        lock (_areaLock)
        {
            _currentArea = null;
        }
    }

    public async Task ChangeAreaAsync(BoundingBox newArea, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Changing tracking area to {Area}", newArea);
        lock (_areaLock)
        {
            _currentArea = newArea;
        }
        // Don't clear vessel store — let stale purge handle it naturally
        // This avoids a data gap when panning the map
        await _aisClient.UpdateSubscriptionAsync(newArea, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VesselTrackingService starting...");

        // Wait for initial area selection if not already set
        bool hasArea;
        lock (_areaLock)
        {
            hasArea = _currentArea is not null;
        }

        if (!hasArea)
        {
            _areaWaiter = new TaskCompletionSource<BoundingBox>();
            using var reg = stoppingToken.Register(() =>
                _areaWaiter.TrySetCanceled());

            try
            {
                var area = await _areaWaiter.Task;
                lock (_areaLock)
                {
                    _currentArea = area;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Periodic stale vessel cleanup — runs until cancellation
        using var purgeTimer = new PeriodicTimer(_purgeInterval);
        try
        {
            while (await purgeTimer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var purged = _vesselStore.PurgeStale(_staleAge);
                    if (purged > 0)
                        _logger.LogDebug("Purged {Count} stale vessels", purged);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error during stale vessel purge");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("VesselTrackingService stopping...");
        }
    }

    private void OnMessageReceived(object? sender, AisMessageEventArgs e)
    {
        try
        {
            var vessel = _vesselStore.AddOrUpdate(e.Mmsi, e.Position, e.StaticData);

            // Calculate ETA if we have position and destination (with throttling)
            if (vessel.CurrentPosition is not null && vessel.StaticData?.Destination is not null)
            {
                if (ShouldRecalculateEta(vessel))
                {
                    var port = ResolvePortCached(vessel.StaticData.Destination);
                    if (port is not null)
                    {
                        vessel.CalculatedEta = EtaCalculator.Calculate(vessel.CurrentPosition, port);
                        _lastEtaPosition[vessel.Mmsi] = (
                            vessel.CurrentPosition.Latitude,
                            vessel.CurrentPosition.Longitude,
                            vessel.CurrentPosition.TrueHeading);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AIS message for MMSI {Mmsi}", e.Mmsi);
        }
    }

    private void OnVesselRemoved(object? sender, Vessel vessel)
    {
        _lastEtaPosition.TryRemove(vessel.Mmsi, out _);
    }

    private bool ShouldRecalculateEta(Vessel vessel)
    {
        if (vessel.CurrentPosition is null) return false;

        if (!_lastEtaPosition.TryGetValue(vessel.Mmsi, out var last))
            return true; // Never calculated

        // Check if heading changed significantly
        var headingDelta = Math.Abs(vessel.CurrentPosition.TrueHeading - last.Heading);
        if (headingDelta > 180) headingDelta = 360 - headingDelta;
        if (headingDelta > _trackingOptions.EtaHeadingThresholdDeg) return true;

        // Check if vessel moved significantly
        var distance = HaversineCalculator.DistanceInNauticalMiles(
            last.Lat, last.Lon,
            vessel.CurrentPosition.Latitude, vessel.CurrentPosition.Longitude);

        return distance > _trackingOptions.EtaDistanceThresholdNm;
    }

    private Port? ResolvePortCached(string destination)
    {
        if (_portCache.TryGetValue(destination, out var cached))
            return cached;

        // Evict oldest entries when cache is full
        if (_portCache.Count >= _trackingOptions.PortCacheMaxSize)
        {
            // Remove roughly half the cache to avoid frequent evictions
            var keysToRemove = _portCache.Keys.Take(_trackingOptions.PortCacheMaxSize / 2).ToList();
            foreach (var key in keysToRemove)
                _portCache.TryRemove(key, out _);
        }

        var port = _portRepository.ResolveDestination(destination);
        _portCache[destination] = port;
        return port;
    }
}
