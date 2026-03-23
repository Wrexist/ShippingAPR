using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

public interface IVesselTrackingService
{
    Task StartTrackingAsync(BoundingBox area, CancellationToken cancellationToken = default);
    Task StopTrackingAsync();
    Task ChangeAreaAsync(BoundingBox newArea, CancellationToken cancellationToken = default);
    ConnectionStatus ConnectionStatus { get; }
    event EventHandler<ConnectionStatus>? ConnectionStatusChanged;
}
