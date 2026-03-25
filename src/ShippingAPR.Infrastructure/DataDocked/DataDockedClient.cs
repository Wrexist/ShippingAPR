using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.DataDocked;

/// <summary>
/// AIS data provider that polls the Data Docked REST API for vessel positions
/// within a bounding box and emits them as events compatible with <see cref="IAisDataProvider"/>.
/// </summary>
public sealed class DataDockedClient : IAisDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly DataDockedOptions _options;
    private readonly ILogger<DataDockedClient> _logger;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private BoundingBox? _currentArea;
    private readonly object _areaLock = new();

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;
    public event EventHandler<AisMessageEventArgs>? MessageReceived;

    public DataDockedClient(
        HttpClient httpClient,
        IOptions<DataDockedOptions> options,
        ILogger<DataDockedClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task ConnectAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("Data Docked API key is not configured. Set it in appsettings.json under DataDocked:ApiKey.");

        lock (_areaLock) { _currentArea = area; }

        _pollCts?.Cancel();
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = PollLoopAsync(_pollCts.Token);

        SetStatus(ConnectionStatus.Connected);
        _logger.LogInformation("DataDocked provider started polling for area {Area}", area);
        return Task.CompletedTask;
    }

    public Task UpdateSubscriptionAsync(BoundingBox newArea, CancellationToken cancellationToken = default)
    {
        lock (_areaLock) { _currentArea = newArea; }
        _logger.LogInformation("DataDocked subscription updated to area {Area}", newArea);
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
                _logger.LogWarning(ex, "DataDocked poll error (consecutive: {Count})", consecutiveErrors);

                if (consecutiveErrors >= 10)
                {
                    _logger.LogError("DataDocked: too many consecutive errors, marking as failed");
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
        // Data Docked uses area search with bounding box coordinates
        var url = $"{_options.BaseUrl}/vessels/area" +
                  $"?min_lat={area.MinLatitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&max_lat={area.MaxLatitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&min_lon={area.MinLongitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&max_lon={area.MaxLongitude.ToString(CultureInfo.InvariantCulture)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        var doc = JsonDocument.Parse(json);

        // Data Docked returns vessels in a "data" or root array
        JsonElement vessels;
        if (doc.RootElement.TryGetProperty("data", out var dataArray))
            vessels = dataArray;
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            vessels = doc.RootElement;
        else
            return;

        foreach (var vessel in vessels.EnumerateArray())
        {
            try
            {
                var mmsi = vessel.TryGetProperty("mmsi", out var mmsiProp) ? mmsiProp.GetInt32() : 0;
                if (mmsi <= 0) continue;

                var lat = vessel.TryGetProperty("latitude", out var latProp) ? latProp.GetDouble() :
                          vessel.TryGetProperty("lat", out var latProp2) ? latProp2.GetDouble() : 0;
                var lon = vessel.TryGetProperty("longitude", out var lonProp) ? lonProp.GetDouble() :
                          vessel.TryGetProperty("lon", out var lonProp2) ? lonProp2.GetDouble() : 0;
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

                MessageReceived?.Invoke(this, new AisMessageEventArgs
                {
                    MessageType = "PositionReport",
                    Mmsi = mmsi,
                    Position = position
                });

                // Emit static data if available
                var name = vessel.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (!string.IsNullOrEmpty(name))
                {
                    MessageReceived?.Invoke(this, new AisMessageEventArgs
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
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse DataDocked vessel entry");
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
