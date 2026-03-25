using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class ShipmentViewModel : ObservableObject, IDisposable
{
    private readonly ShipmentTrackingService _trackingService;
    private readonly IPortRepository _portRepository;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty] private string _newShipmentName = "";
    [ObservableProperty] private string _newOriginPort = "";
    [ObservableProperty] private string _newDestPort = "";

    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _inTransitCount;
    [ObservableProperty] private int _arrivedCount;

    public ObservableCollection<ShipmentItemViewModel> Shipments { get; } = [];

    public ShipmentViewModel(ShipmentTrackingService trackingService, IPortRepository portRepository)
    {
        _trackingService = trackingService;
        _portRepository = portRepository;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    [RelayCommand]
    private void AddShipment()
    {
        if (string.IsNullOrWhiteSpace(NewShipmentName) ||
            string.IsNullOrWhiteSpace(NewOriginPort) ||
            string.IsNullOrWhiteSpace(NewDestPort))
            return;

        _trackingService.AddShipment(NewShipmentName, NewOriginPort, NewDestPort);
        NewShipmentName = "";
        NewOriginPort = "";
        NewDestPort = "";
        Refresh();
    }

    [RelayCommand]
    private void RemoveShipment(string? id)
    {
        if (id is null) return;
        _trackingService.RemoveShipment(id);
        Refresh();
    }

    private void Refresh()
    {
        var shipments = _trackingService.GetAll();

        PendingCount = shipments.Count(s => s.Status == ShipmentStatus.Pending);
        InTransitCount = shipments.Count(s => s.Status == ShipmentStatus.InTransit);
        ArrivedCount = shipments.Count(s => s.Status == ShipmentStatus.Arrived);

        // Full refresh (shipments list is small)
        Shipments.Clear();
        foreach (var shipment in shipments.Reverse())
        {
            var progress = _trackingService.GetProgress(shipment.Id);
            Shipments.Add(new ShipmentItemViewModel(shipment, progress));
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}

public partial class ShipmentItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string RouteText { get; }
    public string StatusText { get; }
    public string StatusColor { get; }
    public double PercentComplete { get; }
    public string ProgressText { get; }
    public string VesselText { get; }
    public string EtaText { get; }

    public ShipmentItemViewModel(Shipment shipment, ShipmentProgress? progress)
    {
        Id = shipment.Id;
        Name = shipment.Name;
        RouteText = $"{shipment.OriginPortName} → {shipment.DestinationPortName}";

        StatusText = shipment.Status.ToString();
        StatusColor = shipment.Status switch
        {
            ShipmentStatus.Pending => "#9E9E9E",
            ShipmentStatus.InTransit => "#60A5FA",
            ShipmentStatus.Delayed => "#FB923C",
            ShipmentStatus.Arrived => "#34D399",
            ShipmentStatus.Cancelled => "#EF4444",
            _ => "#9E9E9E"
        };

        PercentComplete = progress?.PercentComplete ?? 0;
        ProgressText = progress is not null
            ? $"{progress.PercentComplete:F0}% ({progress.DistanceRemainingNm:F0} NM remaining)"
            : "Awaiting vessel assignment";

        VesselText = progress?.AssignedVesselName ?? "Unassigned";
        EtaText = progress?.EstimatedArrival?.ToString("yyyy-MM-dd HH:mm UTC") ?? "--";
    }
}
