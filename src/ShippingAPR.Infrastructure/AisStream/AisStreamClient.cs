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
    private SubscriptionMessage? _lastSubscription;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private long _messageCount;
    private long _parseErrorCount;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public long MessageCount => Interlocked.Read(ref _messageCount);
    public long ParseErrorCount => Interlocked.Read(ref _parseErrorCount);
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
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(_options.KeepAliveIntervalSeconds);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);

            await _webSocket.ConnectAsync(
                new Uri(_options.WebSocketUrl), connectCts.Token);

            await SendSubscriptionAsync(area, cancellationToken);

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

        await SendSubscriptionAsync(newArea, cancellationToken);

        _logger.LogInformation("Updated AIS stream subscription to area {Area}", newArea);
    }

    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();

        // Await the receive task BEFORE disposing the socket, so the loop
        // can observe cancellation and exit cleanly.
        if (_receiveTask is not null)
        {
            try
            {
                var timeout = TimeSpan.FromSeconds(_options.DisconnectTimeoutSeconds);
                await _receiveTask.WaitAsync(timeout);
            }
            catch (OperationCanceledException) { /* Expected when cancellation triggers */ }
            catch (TimeoutException)
            {
                _logger.LogWarning("Receive task did not complete within {Timeout}s timeout during disconnect",
                    _options.DisconnectTimeoutSeconds);
            }
            _receiveTask = null;
        }

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

        SetStatus(ConnectionStatus.Disconnected);
    }

    /// <summary>
    /// Main receive loop with built-in reconnection logic.
    /// Uses an outer loop for reconnection instead of recursion to avoid stack overflow.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var needsReconnect = false;

        while (!ct.IsCancellationRequested)
        {
            if (needsReconnect)
            {
                var reconnected = await TryReconnectAsync(ct);
                if (!reconnected)
                    return; // Cancellation requested or permanent failure
                needsReconnect = false;
            }

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
                                needsReconnect = true;
                                break;
                            }

                            messageBuffer.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (needsReconnect) break;

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var json = Encoding.UTF8.GetString(
                                messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                            ProcessMessage(json);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (WebSocketException ex)
                    {
                        _logger.LogWarning(ex, "WebSocket error, will reconnect...");
                        needsReconnect = true;
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Attempts to reconnect with exponential backoff. Returns true if reconnected, false if cancelled.
    /// </summary>
    private async Task<bool> TryReconnectAsync(CancellationToken ct)
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
                return false;
            }

            try
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(_options.KeepAliveIntervalSeconds);

                await _webSocket.ConnectAsync(
                    new Uri(_options.WebSocketUrl), ct);

                // Re-send subscription so the server knows what data to send.
                if (_lastSubscription is not null)
                {
                    var json = JsonSerializer.Serialize(_lastSubscription);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        ct);
                }

                SetStatus(ConnectionStatus.Connected);
                _logger.LogInformation("Reconnected to AIS stream");
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection attempt failed");
                delay = Math.Min(delay * 2, maxDelay);
            }
        }

        return false;
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
                Interlocked.Increment(ref _messageCount);
                MessageReceived?.Invoke(this, args);
            }
        }
        catch (JsonException ex)
        {
            Interlocked.Increment(ref _parseErrorCount);
            _logger.LogWarning(ex, "Failed to parse AIS message (total errors: {Count})",
                Interlocked.Read(ref _parseErrorCount));
        }
    }

    private async Task SendSubscriptionAsync(BoundingBox area, CancellationToken cancellationToken)
    {
        var subscription = new SubscriptionMessage
        {
            ApiKey = _options.ApiKey,
            BoundingBoxes = [area.ToAisStreamFormat()],
            FilterMessageTypes = ["PositionReport", "ShipStaticData"]
        };
        _lastSubscription = subscription;

        var json = JsonSerializer.Serialize(subscription);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket!.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
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
