using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure;

/// <summary>
/// Wraps a primary and optional fallback <see cref="IAisDataProvider"/>.
/// Automatically switches to the fallback when the primary enters <see cref="ConnectionStatus.Failed"/>.
/// </summary>
public sealed class FallbackAisProvider : IAisDataProvider, IDisposable
{
    private readonly IAisDataProvider _primary;
    private readonly IAisDataProvider? _fallback;
    private readonly ILogger<FallbackAisProvider> _logger;
    private IAisDataProvider _active;
    private BoundingBox? _lastArea;
    private bool _usingFallback;

    public ConnectionStatus Status => _active.Status;
    public event EventHandler<ConnectionStatus>? ConnectionStatusChanged;
    public event EventHandler<AisMessageEventArgs>? MessageReceived;

    public string ActiveProviderName => _usingFallback ? "Fallback" : "Primary";

    public FallbackAisProvider(
        IAisDataProvider primary,
        IAisDataProvider? fallback,
        ILogger<FallbackAisProvider> logger)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
        _active = primary;

        SubscribeTo(_active);
    }

    public async Task ConnectAsync(BoundingBox area, CancellationToken cancellationToken = default)
    {
        _lastArea = area;
        try
        {
            await _active.ConnectAsync(area, cancellationToken);
        }
        catch when (_fallback is not null && !_usingFallback)
        {
            _logger.LogWarning("Primary provider failed to connect, switching to fallback");
            await SwitchToFallbackAsync(area, cancellationToken);
        }
    }

    public Task UpdateSubscriptionAsync(BoundingBox newArea, CancellationToken cancellationToken = default)
    {
        _lastArea = newArea;
        return _active.UpdateSubscriptionAsync(newArea, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        await _active.DisconnectAsync();
    }

    private void OnConnectionStatusChanged(object? sender, ConnectionStatus status)
    {
        if (status == ConnectionStatus.Failed && _fallback is not null && !_usingFallback && _lastArea is not null)
        {
            _logger.LogWarning("Primary provider failed, switching to fallback provider");
            _ = SwitchToFallbackAsync(_lastArea, CancellationToken.None);
            return;
        }

        ConnectionStatusChanged?.Invoke(this, status);
    }

    private void OnMessageReceived(object? sender, AisMessageEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private async Task SwitchToFallbackAsync(BoundingBox area, CancellationToken ct)
    {
        UnsubscribeFrom(_active);

        try { await _active.DisconnectAsync(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error disconnecting primary during fallback switch"); }

        _active = _fallback!;
        _usingFallback = true;
        SubscribeTo(_active);

        try
        {
            await _active.ConnectAsync(area, ct);
            _logger.LogInformation("Successfully switched to fallback provider");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback provider also failed to connect");
            ConnectionStatusChanged?.Invoke(this, ConnectionStatus.Failed);
        }
    }

    private void SubscribeTo(IAisDataProvider provider)
    {
        provider.ConnectionStatusChanged += OnConnectionStatusChanged;
        provider.MessageReceived += OnMessageReceived;
    }

    private void UnsubscribeFrom(IAisDataProvider provider)
    {
        provider.ConnectionStatusChanged -= OnConnectionStatusChanged;
        provider.MessageReceived -= OnMessageReceived;
    }

    public void Dispose()
    {
        UnsubscribeFrom(_active);
        (_primary as IDisposable)?.Dispose();
        (_fallback as IDisposable)?.Dispose();
    }
}
