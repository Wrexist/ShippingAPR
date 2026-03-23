using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

public interface IVesselEnrichmentClient
{
    Task<VesselStaticData?> GetVesselDetailsAsync(int mmsi, CancellationToken cancellationToken = default);
}
