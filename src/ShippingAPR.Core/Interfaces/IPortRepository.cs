using ShippingAPR.Core.Models;

namespace ShippingAPR.Core.Interfaces;

public interface IPortRepository
{
    Port? FindByLocode(string locode);
    Port? FindByName(string name);
    Port? FindNearest(double latitude, double longitude, double maxDistanceNm = 100.0);
    IEnumerable<Port> SearchPorts(string query);
    IReadOnlyList<Port> GetAll();
}
