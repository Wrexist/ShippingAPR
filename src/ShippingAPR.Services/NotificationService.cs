using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;

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
    private const int MaxHistory = 100;

    public IReadOnlyList<NotificationMessage> History
    {
        get { lock (_historyLock) return _history.ToList(); }
    }

    public NotificationService(
        AreaMonitorService areaMonitor,
        ILogger<NotificationService> logger)
    {
        _logger = logger;

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
            while (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }

        _logger.LogDebug("Notification: {Title} - {Body}", message.Title, message.Body);
        WeakReferenceMessenger.Default.Send(new NotificationPublished(message));
    }
}
