using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Providers;

/// <summary>
/// Abstract base class for REST-based AIS data providers that poll an API on a timer
/// and emit vessel data as <see cref="IAisDataProvider"/> events.
/// </summary>
public abstract class PollingAisProviderBase : IAisDataProvider, IDisposable
{
    private readonly ILogger _logger;
    private readonly int _pollIntervalSeconds;
    private readonly int _maxConsecutiveErrors;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private BoundingBox? _currentArea;
    private readonly object _areaLock = new();

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;
    public event EventHandler<AisMessageEventArgs>? MessageReceived;

    protected abstract string ProviderName { get; }
    protected abstract string ApiKey { get; }
    protected abstract string MissingKeyMessage { get; }

    protected PollingAisProviderBase(
        ILogger logger,
        int pollIntervalSeconds = 15,
        int maxConsecutiveErrors = 10)
    {
        _logger = logger;
        _pollIntervalSeconds = pollIntervalSeconds;
        _maxConsecutiveErrors = maxConsecutiveErrors;
    }

    public Task ConnectAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ApiKey))
            throw new InvalidOperationException(MissingKeyMessage);

        lock (_areaLock) { _currentArea = area; }

        _pollCts?.Cancel();
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = PollLoopAsync(_pollCts.Token);

        SetStatus(ConnectionStatus.Connected);
        _logger.LogInformation("{Provider} started polling for area {Area}", ProviderName, area);
        return Task.CompletedTask;
    }

    public Task UpdateSubscriptionAsync(BoundingBox newArea, CancellationToken cancellationToken = default)
    {
        lock (_areaLock) { _currentArea = newArea; }
        _logger.LogInformation("{Provider} subscription updated to area {Area}", ProviderName, newArea);
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

    /// <summary>
    /// Implement this to fetch vessel data from the provider's REST API and emit events.
    /// Call <see cref="EmitMessage"/> for each vessel.
    /// </summary>
    protected abstract Task FetchAndEmitAsync(BoundingBox area, CancellationToken ct);

    protected void EmitMessage(AisMessageEventArgs args)
    {
        MessageReceived?.Invoke(this, args);
    }

    protected void SetStatus(ConnectionStatus status)
    {
        Status = status;
        ConnectionStatusChanged?.Invoke(this, status);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_pollIntervalSeconds);
        var consecutiveErrors = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);

                BoundingBox? area;
                lock (_areaLock) { area = _currentArea; }
                // No area yet (subscription update raced ahead of connect) —
                // skip this tick rather than dereferencing null.
                if (area is null) continue;

                await FetchAndEmitAsync(area, ct);
                consecutiveErrors = 0;

                if (Status != ConnectionStatus.Connected)
                    SetStatus(ConnectionStatus.Connected);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                _logger.LogWarning(ex, "{Provider} poll error (consecutive: {Count})", ProviderName, consecutiveErrors);

                if (consecutiveErrors >= _maxConsecutiveErrors)
                {
                    _logger.LogError("{Provider}: too many consecutive errors, marking as failed", ProviderName);
                    SetStatus(ConnectionStatus.Failed);
                    return;
                }

                SetStatus(ConnectionStatus.Reconnecting);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(consecutiveErrors * 2, 30)), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
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
