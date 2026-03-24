using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ShippingAPR.Services;

public sealed class NotificationMessage
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required NotificationType Type { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public enum NotificationType
{
    Info,
    VesselEntered,
    VesselLeft,
    ConnectionChanged,
    Warning
}

public sealed class NotificationPublished : ValueChangedMessage<NotificationMessage>
{
    public NotificationPublished(NotificationMessage value) : base(value) { }
}

public sealed class NotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly object _historyLock = new();
    private readonly List<NotificationMessage> _history = [];
    private readonly int _maxHistory;

    public IReadOnlyList<NotificationMessage> History
    {
        get { lock (_historyLock) return _history.ToList(); }
    }

    public NotificationService(
        AreaMonitorService areaMonitor,
        ILogger<NotificationService> logger,
        IOptions<TrackingOptions> trackingOptions)
    {
        _logger = logger;
        _maxHistory = trackingOptions.Value.MaxNotificationHistory;

        areaMonitor.VesselAreaChanged += (_, evt) =>
        {
            var msg = new NotificationMessage
            {
                Title = evt.Entered ? "Vessel Entered Area" : "Vessel Left Area",
                Body = $"{evt.Vessel.DisplayName} (MMSI {evt.Vessel.Mmsi})" +
                       (evt.Vessel.CurrentPosition is not null
                           ? $" at {evt.Vessel.CurrentPosition.SpeedOverGround:F1} kn"
                           : ""),
                Type = evt.Entered ? NotificationType.VesselEntered : NotificationType.VesselLeft
            };

            Publish(msg);
        };
    }

    public void Publish(NotificationMessage message)
    {
        lock (_historyLock)
        {
            _history.Add(message);
            if (_history.Count > _maxHistory)
            {
                var excess = _history.Count - _maxHistory;
                _history.RemoveRange(0, excess);
            }
        }

        _logger.LogDebug("Notification: {Title} - {Body}", message.Title, message.Body);
        WeakReferenceMessenger.Default.Send(new NotificationPublished(message));
    }
}
