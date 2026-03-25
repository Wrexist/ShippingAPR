using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

public interface ITideDataClient
{
    Task<TideData?> GetTideDataAsync(string portLocode, string portName, double latitude, double longitude, CancellationToken ct = default);
}
