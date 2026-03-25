using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class NotificationCenterViewModel : ObservableObject
{
    private readonly NotificationService _notificationService;

    [ObservableProperty]
    private int _unreadCount;

    public ObservableCollection<NotificationMessage> Notifications { get; } = [];

    public NotificationCenterViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;

        // Load existing history
        foreach (var msg in _notificationService.History)
            Notifications.Insert(0, msg);

        // Subscribe to new notifications
        WeakReferenceMessenger.Default.Register<NotificationPublished>(this, (_, msg) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Notifications.Insert(0, msg.Value);
                if (Notifications.Count > 100)
                    Notifications.RemoveAt(Notifications.Count - 1);
                UnreadCount++;
            });
        });
    }

    [RelayCommand]
    private void ClearAll()
    {
        Notifications.Clear();
        UnreadCount = 0;
    }

    [RelayCommand]
    private void MarkAllRead()
    {
        UnreadCount = 0;
    }
}
