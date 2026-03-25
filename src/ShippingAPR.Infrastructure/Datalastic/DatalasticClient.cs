using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Datalastic;

/// <summary>
/// AIS data provider that polls the Datalastic REST API for vessel positions
/// within a bounding box and emits them as events compatible with <see cref="IAisDataProvider"/>.
/// </summary>
public sealed class DatalasticClient : IAisDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly DatalasticOptions _options;
    private readonly ILogger<DatalasticClient> _logger;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private BoundingBox? _currentArea;
    private readonly object _areaLock = new();

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;
    public event EventHandler<AisMessageEventArgs>? MessageReceived;

    public DatalasticClient(
        HttpClient httpClient,
        IOptions<DatalasticOptions> options,
        ILogger<DatalasticClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task ConnectAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("Datalastic API key is not configured. Set it in appsettings.json under Datalastic:ApiKey.");

        lock (_areaLock) { _currentArea = area; }

        _pollCts?.Cancel();
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = PollLoopAsync(_pollCts.Token);

        SetStatus(ConnectionStatus.Connected);
        _logger.LogInformation("Datalastic provider started polling for area {Area}", area);
        return Task.CompletedTask;
    }

    public Task UpdateSubscriptionAsync(BoundingBox newArea, CancellationToken cancellationToken = default)
    {
        lock (_areaLock) { _currentArea = newArea; }
        _logger.LogInformation("Datalastic subscription updated to area {Area}", newArea);
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        _pollCts?.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            _pollTask = null;
        }
        _pollCts?.Dispose();
        _pollCts = null;
        SetStatus(ConnectionStatus.Disconnected);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        var consecutiveErrors = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);

                BoundingBox area;
                lock (_areaLock) { area = _currentArea!; }

                await FetchAndEmitAsync(area, ct);
                consecutiveErrors = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                _logger.LogWarning(ex, "Datalastic poll error (consecutive: {Count})", consecutiveErrors);

                if (consecutiveErrors >= 10)
                {
                    _logger.LogError("Datalastic: too many consecutive errors, marking as failed");
                    SetStatus(ConnectionStatus.Failed);
                    return;
                }

                SetStatus(ConnectionStatus.Reconnecting);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(consecutiveErrors * 2, 30)), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task FetchAndEmitAsync(BoundingBox area, CancellationToken ct)
    {
        var url = $"{_options.BaseUrl}/vessel_find" +
                  $"?api-key={_options.ApiKey}" +
                  $"&params.min_latitude={area.MinLatitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&params.max_latitude={area.MaxLatitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&params.min_longitude={area.MinLongitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&params.max_longitude={area.MaxLongitude.ToString(CultureInfo.InvariantCulture)}";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        var response = await _httpClient.GetStringAsync(url, timeoutCts.Token);
        var doc = JsonDocument.Parse(response);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            return;

        foreach (var vessel in data.EnumerateArray())
        {
            try
            {
                var mmsi = vessel.TryGetProperty("mmsi", out var mmsiProp) ? mmsiProp.GetInt32() : 0;
                if (mmsi <= 0) continue;

                var lat = vessel.TryGetProperty("lat", out var latProp) ? latProp.GetDouble() : 0;
                var lon = vessel.TryGetProperty("lon", out var lonProp) ? lonProp.GetDouble() : 0;
                var speed = vessel.TryGetProperty("speed", out var speedProp) ? speedProp.GetDouble() : 0;
                var course = vessel.TryGetProperty("course", out var courseProp) ? courseProp.GetDouble() : 0;
                var heading = vessel.TryGetProperty("heading", out var headingProp) ? headingProp.GetDouble() : 0;

                var position = new VesselPosition
                {
                    Latitude = lat,
                    Longitude = lon,
                    SpeedOverGround = speed,
                    CourseOverGround = course,
                    TrueHeading = heading > 0 ? heading : course,
                    Timestamp = DateTime.UtcNow
                };

                var args = new AisMessageEventArgs
                {
                    MessageType = "PositionReport",
                    Mmsi = mmsi,
                    Position = position
                };

                MessageReceived?.Invoke(this, args);

                // Also emit static data if available
                var name = vessel.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (!string.IsNullOrEmpty(name))
                {
                    var staticArgs = new AisMessageEventArgs
                    {
                        MessageType = "ShipStaticData",
                        Mmsi = mmsi,
                        StaticData = new VesselStaticData
                        {
                            Name = name,
                            CallSign = vessel.TryGetProperty("callsign", out var csProp) ? csProp.GetString() : null,
                            ImoNumber = vessel.TryGetProperty("imo", out var imoProp) && imoProp.ValueKind == JsonValueKind.Number ? imoProp.GetInt32() : 0,
                            Destination = vessel.TryGetProperty("destination", out var destProp) ? destProp.GetString() : null
                        }
                    };
                    MessageReceived?.Invoke(this, staticArgs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse Datalastic vessel entry");
            }
        }

        if (Status != ConnectionStatus.Connected)
            SetStatus(ConnectionStatus.Connected);
    }

    private void SetStatus(ConnectionStatus status)
    {
        Status = status;
        ConnectionStatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        var task = _pollTask;
        if (task is not null)
        {
            try { task.Wait(TimeSpan.FromSeconds(3)); }
            catch { /* best effort */ }
        }
        _pollCts?.Dispose();
    }
}
