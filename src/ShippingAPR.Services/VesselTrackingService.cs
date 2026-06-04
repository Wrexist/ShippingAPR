using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Core.Validation;
using ShippingAPR.Infrastructure.Ports;

namespace ShippingAPR.Services;

public sealed class VesselTrackingService : BackgroundService, IVesselTrackingService
{
    private readonly IAisDataProvider _aisClient;
    private readonly IVesselStore _vesselStore;
    private readonly PortRepository _portRepository;
    private readonly IVesselEnrichmentClient? _enrichmentClient;
    private readonly ILogger<VesselTrackingService> _logger;
    private readonly TrackingOptions _trackingOptions;
    private readonly TimeSpan _purgeInterval;
    private readonly TimeSpan _staleAge;

    // ETA calculation caching — only recalculate when vessel moves significantly
    private readonly ConcurrentDictionary<int, (double Lat, double Lon, double Heading)> _lastEtaPosition = new();

    // Port resolution caching — avoid O(n) port scan on every message
    private readonly ConcurrentDictionary<string, Port?> _portCache = new(StringComparer.OrdinalIgnoreCase);

    // Vessel enrichment — rate-limited queue for fetching missing static data
    private readonly Channel<int> _enrichmentQueue = Channel.CreateBounded<int>(new BoundedChannelOptions(200)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly ConcurrentDictionary<int, bool> _enrichmentRequested = new();

    private readonly object _areaLock = new();
    private BoundingBox? _currentArea;
    private TaskCompletionSource<BoundingBox>? _areaWaiter;

    private readonly EventHandler<ConnectionStatus> _onConnectionStatusChanged;

    public ConnectionStatus ConnectionStatus => _aisClient.Status;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;

    public VesselTrackingService(
        IAisDataProvider aisClient,
        IVesselStore vesselStore,
        PortRepository portRepository,
        ILogger<VesselTrackingService> logger,
        IOptions<TrackingOptions> trackingOptions,
        IVesselEnrichmentClient? enrichmentClient = null)
    {
        _aisClient = aisClient;
        _vesselStore = vesselStore;
        _portRepository = portRepository;
        _enrichmentClient = enrichmentClient;
        _logger = logger;
        _trackingOptions = trackingOptions.Value;
        _purgeInterval = TimeSpan.FromMinutes(_trackingOptions.PurgeIntervalMinutes);
        _staleAge = TimeSpan.FromMinutes(_trackingOptions.StaleAgeMinutes);

        _onConnectionStatusChanged = (_, status) =>
            ConnectionStatusChanged?.Invoke(this, status);
        _aisClient.ConnectionStatusChanged += _onConnectionStatusChanged;

        _aisClient.MessageReceived += OnMessageReceived;

        _vesselStore.VesselRemoved += OnVesselRemoved;
    }

    public Task StartTrackingAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
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

        // Start enrichment processor in background if enrichment client is available
        var enrichmentTask = _enrichmentClient is not null
            ? ProcessEnrichmentQueueAsync(stoppingToken)
            : Task.CompletedTask;

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

        await enrichmentTask;
    }

    private void OnMessageReceived(object? sender, AisMessageEventArgs e)
    {
        // Skip malformed MMSIs without throwing on the AIS hot path — a bad
        // value is a data-quality issue, not an exceptional condition.
        if (!MmsiValidator.IsValid(e.Mmsi))
        {
            _logger.LogDebug("Skipping AIS message with invalid MMSI {Mmsi}", e.Mmsi);
            return;
        }

        try
        {
            var vessel = _vesselStore.AddOrUpdate(e.Mmsi, e.Position, e.StaticData);

            // Queue enrichment for vessels missing static data
            if (_enrichmentClient is not null &&
                e.MessageType == "PositionReport" &&
                vessel.StaticData is null &&
                _enrichmentRequested.TryAdd(e.Mmsi, true))
            {
                _enrichmentQueue.Writer.TryWrite(e.Mmsi);
            }

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
                    else
                    {
                        _logger.LogDebug("Could not resolve destination '{Destination}' to a port for MMSI {Mmsi}",
                            vessel.StaticData.Destination, vessel.Mmsi);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AIS message for MMSI {Mmsi}", e.Mmsi);
        }
    }

    private async Task ProcessEnrichmentQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var mmsi in _enrichmentQueue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var data = await _enrichmentClient!.GetVesselDetailsAsync(mmsi, ct);
                    if (data is not null)
                    {
                        _vesselStore.AddOrUpdate(mmsi, position: null, staticData: data);
                        _logger.LogDebug("Enriched vessel MMSI {Mmsi} with static data", mmsi);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to enrich MMSI {Mmsi}", mmsi);
                }

                // Rate limit enrichment requests
                await Task.Delay(_trackingOptions.EnrichmentDelayMs, ct);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }

    private void OnVesselRemoved(object? sender, Vessel vessel)
    {
        _lastEtaPosition.TryRemove(vessel.Mmsi, out _);
        _enrichmentRequested.TryRemove(vessel.Mmsi, out _);
    }

    private bool ShouldRecalculateEta(Vessel vessel)
    {
        if (vessel.CurrentPosition is null) return false;

        if (!_lastEtaPosition.TryGetValue(vessel.Mmsi, out var last))
            return true; // Never calculated

        // Check if heading changed significantly
        var headingDelta = BearingCalculator.AngleDifference(vessel.CurrentPosition.TrueHeading, last.Heading);
        if (headingDelta > _trackingOptions.EtaHeadingThresholdDeg) return true;

        // Check if vessel moved significantly
        var distance = HaversineCalculator.DistanceInNauticalMiles(
            last.Lat, last.Lon,
            vessel.CurrentPosition.Latitude, vessel.CurrentPosition.Longitude);

        return distance > _trackingOptions.EtaDistanceThresholdNm;
    }

    public override void Dispose()
    {
        _aisClient.ConnectionStatusChanged -= _onConnectionStatusChanged;
        _aisClient.MessageReceived -= OnMessageReceived;
        _vesselStore.VesselRemoved -= OnVesselRemoved;
        base.Dispose();
    }

    private readonly object _portCacheLock = new();

    private Port? ResolvePortCached(string destination)
    {
        if (_portCache.TryGetValue(destination, out var cached))
            return cached;

        // Evict entries when cache is full (lock prevents concurrent eviction storms)
        if (_portCache.Count >= _trackingOptions.PortCacheMaxSize)
        {
            lock (_portCacheLock)
            {
                // Double-check after acquiring lock
                if (_portCache.Count >= _trackingOptions.PortCacheMaxSize)
                {
                    var evictCount = _trackingOptions.PortCacheMaxSize / 2;
                    var removed = 0;
                    foreach (var key in _portCache.Keys.ToArray())
                    {
                        if (removed >= evictCount) break;
                        if (_portCache.TryRemove(key, out _))
                            removed++;
                    }
                    _logger.LogDebug("Port cache eviction: removed {Count} entries (was at capacity {Max})",
                        removed, _trackingOptions.PortCacheMaxSize);
                }
            }
        }

        var port = _portRepository.ResolveDestination(destination);
        _portCache[destination] = port;
        return port;
    }
}
