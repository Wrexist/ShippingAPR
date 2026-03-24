using FluentAssertions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class VesselStoreVesselRemovedTests
{
    private readonly VesselStore _store = new();

    [Fact]
    public void PurgeStale_FiresVesselRemovedForEachPurgedVessel()
    {
        var removedMmsis = new List<int>();
        _store.VesselRemoved += (_, v) => removedMmsis.Add(v.Mmsi);

        // Add two stale vessels
        var v1 = _store.AddOrUpdate(1, CreatePosition(57.7, 11.9), null);
        v1.LastUpdated = DateTime.UtcNow.AddMinutes(-15);
        var v2 = _store.AddOrUpdate(2, CreatePosition(57.8, 12.0), null);
        v2.LastUpdated = DateTime.UtcNow.AddMinutes(-15);

        // Add one fresh vessel
        _store.AddOrUpdate(3, CreatePosition(57.9, 12.1), null);

        var purged = _store.PurgeStale(TimeSpan.FromMinutes(10));

        purged.Should().Be(2);
        removedMmsis.Should().BeEquivalentTo([1, 2]);
        _store.Count.Should().Be(1);
    }

    [Fact]
    public void PurgeStale_NoStaleVessels_DoesNotFireEvent()
    {
        var eventFired = false;
        _store.VesselRemoved += (_, _) => eventFired = true;

        _store.AddOrUpdate(1, CreatePosition(57.7, 11.9), null);

        var purged = _store.PurgeStale(TimeSpan.FromMinutes(10));

        purged.Should().Be(0);
        eventFired.Should().BeFalse();
    }

    private static VesselPosition CreatePosition(double lat, double lon) =>
        new()
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = 10.0,
            CourseOverGround = 0,
            TrueHeading = 0,
            Status = NavigationalStatus.UnderWayUsingEngine,
            Timestamp = DateTime.UtcNow
        };
}
