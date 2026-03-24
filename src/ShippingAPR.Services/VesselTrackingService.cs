using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private readonly TimeSpan _purgeInterval = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _staleAge = TimeSpan.FromMinutes(10);

    // ETA calculation caching — only recalculate when vessel moves significantly
    private readonly Dictionary<int, (double Lat, double Lon, double Heading)> _lastEtaPosition = new();
    private const double EtaDistanceThresholdNm = 0.5;
    private const double EtaHeadingThresholdDeg = 5.0;

    // Port resolution caching — avoid O(n) port scan on every message
    private readonly Dictionary<string, Port?> _portCache = new(StringComparer.OrdinalIgnoreCase);

    private BoundingBox? _currentArea;
    private TaskCompletionSource<BoundingBox>? _areaWaiter;

    public ConnectionStatus ConnectionStatus => _aisClient.Status;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;

    public VesselTrackingService(
        IAisStreamClient aisClient,
        IVesselStore vesselStore,
        PortRepository portRepository,
        ILogger<VesselTrackingService> logger)
    {
        _aisClient = aisClient;
        _vesselStore = vesselStore;
        _portRepository = portRepository;
        _logger = logger;

        _aisClient.ConnectionStatusChanged += (_, status) =>
            ConnectionStatusChanged?.Invoke(this, status);

        _aisClient.MessageReceived += OnMessageReceived;
    }

    public Task StartTrackingAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        _currentArea = area;
        _areaWaiter?.TrySetResult(area);
        return _aisClient.ConnectAsync(area, cancellationToken);
    }

    public async Task StopTrackingAsync()
    {
        await _aisClient.DisconnectAsync();
        _currentArea = null;
    }

    public async Task ChangeAreaAsync(BoundingBox newArea, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Changing tracking area to {Area}", newArea);
        _currentArea = newArea;
        // Don't clear vessel store — let stale purge handle it naturally
        // This avoids a data gap when panning the map
        await _aisClient.UpdateSubscriptionAsync(newArea, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VesselTrackingService starting...");

        // Wait for initial area selection if not already set
        if (_currentArea is null)
        {
            _areaWaiter = new TaskCompletionSource<BoundingBox>();
            using var reg = stoppingToken.Register(() =>
                _areaWaiter.TrySetCanceled());

            try
            {
                _currentArea = await _areaWaiter.Task;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Periodic stale vessel cleanup
        using var purgeTimer = new PeriodicTimer(_purgeInterval);
        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await purgeTimer.WaitForNextTickAsync(stoppingToken);
                    var purged = ((VesselStore)_vesselStore).PurgeStale(_staleAge);
                    if (purged > 0)
                        _logger.LogDebug("Purged {Count} stale vessels", purged);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, stoppingToken);

        // Keep alive until stopped
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
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

    private bool ShouldRecalculateEta(Vessel vessel)
    {
        if (vessel.CurrentPosition is null) return false;

        if (!_lastEtaPosition.TryGetValue(vessel.Mmsi, out var last))
            return true; // Never calculated

        // Check if heading changed significantly
        var headingDelta = Math.Abs(vessel.CurrentPosition.TrueHeading - last.Heading);
        if (headingDelta > 180) headingDelta = 360 - headingDelta;
        if (headingDelta > EtaHeadingThresholdDeg) return true;

        // Check if vessel moved significantly
        var distance = HaversineCalculator.DistanceInNauticalMiles(
            last.Lat, last.Lon,
            vessel.CurrentPosition.Latitude, vessel.CurrentPosition.Longitude);

        return distance > EtaDistanceThresholdNm;
    }

    private Port? ResolvePortCached(string destination)
    {
        if (_portCache.TryGetValue(destination, out var cached))
            return cached;

        var port = _portRepository.ResolveDestination(destination);
        _portCache[destination] = port;
        return port;
    }
}
