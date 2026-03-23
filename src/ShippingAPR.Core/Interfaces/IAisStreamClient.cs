using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

public interface IAisStreamClient
{
    Task ConnectAsync(BoundingBox area, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task UpdateSubscriptionAsync(BoundingBox newArea, CancellationToken cancellationToken = default);
    ConnectionStatus Status { get; }
    event EventHandler<ConnectionStatus>? ConnectionStatusChanged;
    event EventHandler<AisMessageEventArgs>? MessageReceived;
}

public sealed class AisMessageEventArgs : EventArgs
{
    public required string MessageType { get; init; }
    public VesselPosition? Position { get; init; }
    public VesselStaticData? StaticData { get; init; }
    public required int Mmsi { get; init; }
}
