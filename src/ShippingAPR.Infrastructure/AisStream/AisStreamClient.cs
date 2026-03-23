using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.AisStream.Messages;
using ShippingAPR.Infrastructure.Mapping;

namespace ShippingAPR.Infrastructure.AisStream;

public sealed class AisStreamClient : IAisStreamClient, IDisposable
{
    private readonly AisStreamOptions _options;
    private readonly ILogger<AisStreamClient> _logger;
    private readonly AisMessageMapper _mapper;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;
    public event EventHandler<AisMessageEventArgs>? MessageReceived;

    public AisStreamClient(
        IOptions<AisStreamOptions> options,
        ILogger<AisStreamClient> logger,
        AisMessageMapper mapper)
    {
        _options = options.Value;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task ConnectAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();

        SetStatus(ConnectionStatus.Connecting);

        try
        {
            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            await _webSocket.ConnectAsync(
                new Uri(_options.WebSocketUrl), cancellationToken);

            var subscription = new SubscriptionMessage
            {
                ApiKey = _options.ApiKey,
                BoundingBoxes = [area.ToAisStreamFormat()],
                FilterMessageTypes = ["PositionReport", "ShipStaticData"]
            };

            var json = JsonSerializer.Serialize(subscription);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);

            SetStatus(ConnectionStatus.Connected);
            _logger.LogInformation("Connected to AIS stream for area {Area}", area);

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to AIS stream");
            SetStatus(ConnectionStatus.Error);
            throw;
        }
    }

    public async Task UpdateSubscriptionAsync(BoundingBox newArea, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            await ConnectAsync(newArea, cancellationToken);
            return;
        }

        var subscription = new SubscriptionMessage
        {
            ApiKey = _options.ApiKey,
            BoundingBoxes = [newArea.ToAisStreamFormat()],
            FilterMessageTypes = ["PositionReport", "ShipStaticData"]
        };

        var json = JsonSerializer.Serialize(subscription);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

        _logger.LogInformation("Updated AIS stream subscription to area {Area}", newArea);
    }

    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing WebSocket");
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _receiveCts?.Dispose();
        _receiveCts = null;

        if (_receiveTask is not null)
        {
            try { await _receiveTask; }
            catch (OperationCanceledException) { }
            _receiveTask = null;
        }

        SetStatus(ConnectionStatus.Disconnected);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.ReceiveBufferSize);
        try
        {
            using var messageBuffer = new MemoryStream();

            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    messageBuffer.SetLength(0);
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), ct);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInformation("WebSocket closed by server");
                            await ReconnectAsync(ct);
                            return;
                        }

                        messageBuffer.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(
                            messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                        ProcessMessage(json);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    _logger.LogWarning(ex, "WebSocket error, reconnecting...");
                    await ReconnectAsync(ct);
                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var aisMessage = JsonSerializer.Deserialize<AisMessage>(json);
            if (aisMessage is null) return;

            var args = _mapper.Map(aisMessage);
            if (args is not null)
            {
                MessageReceived?.Invoke(this, args);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse AIS message");
        }
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        var delay = 1;
        var maxDelay = _options.ReconnectMaxDelaySeconds;

        while (!ct.IsCancellationRequested)
        {
            SetStatus(ConnectionStatus.Reconnecting);
            _logger.LogInformation("Reconnecting in {Delay}s...", delay);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                await _webSocket.ConnectAsync(
                    new Uri(_options.WebSocketUrl), ct);

                SetStatus(ConnectionStatus.Connected);
                _logger.LogInformation("Reconnected to AIS stream");

                // Re-enter receive loop
                await ReceiveLoopAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection attempt failed");
                delay = Math.Min(delay * 2, maxDelay);
            }
        }
    }

    private void SetStatus(ConnectionStatus status)
    {
        Status = status;
        ConnectionStatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _webSocket?.Dispose();
        _receiveCts?.Dispose();
    }
}
