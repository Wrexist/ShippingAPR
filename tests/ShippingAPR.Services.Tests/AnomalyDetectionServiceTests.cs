using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class AnomalyDetectionServiceTests
{
    private readonly VesselStore _store = new();
    private readonly AnomalyDetectionService _sut;

    public AnomalyDetectionServiceTests()
    {
        var area = new AreaMonitorService(_store, NullLogger<AreaMonitorService>.Instance);
        var notifications = new NotificationService(area, NullLogger<NotificationService>.Instance, Options.Create(new TrackingOptions()));
        _sut = new AnomalyDetectionService(_store, notifications, NullLogger<AnomalyDetectionService>.Instance);
    }

    [Fact]
    public void PositionJump_ImpossibleSpeed_Flagged()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _sut.CheckPositionJump(1, 57.0, 11.0, 10, t).Should().BeNull(); // first fix, no prior

        // 60 NM (1° lat) in 1 minute → ~3600 kn implied → jump
        var a = _sut.CheckPositionJump(1, 58.0, 11.0, 10, t.AddMinutes(1));

        a.Should().NotBeNull();
        a!.Type.Should().Be(AnomalyType.PositionJump);
    }

    [Fact]
    public void PositionJump_NormalMovement_NotFlagged()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _sut.CheckPositionJump(1, 57.0, 11.0, 10, t);

        // ~10 NM in 1 hour = 10 kn
        _sut.CheckPositionJump(1, 57.1667, 11.0, 10, t.AddHours(1)).Should().BeNull();
    }

    [Fact]
    public void PositionJump_TinyJitter_NotFlagged()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _sut.CheckPositionJump(1, 57.0, 11.0, 10, t);

        // ~0.05 NM in 2 s — high implied speed but below the min jump distance → ignored
        _sut.CheckPositionJump(1, 57.0008, 11.0, 10, t.AddSeconds(2)).Should().BeNull();
    }

    [Fact]
    public void AisGap_MovingVessel_Flagged()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _sut.CheckPositionJump(1, 57.0, 11.0, 12, t); // last known: moving 12 kn

        var a = _sut.CheckAisGap(1, t.AddMinutes(15));

        a.Should().NotBeNull();
        a!.Type.Should().Be(AnomalyType.AisGap);
    }

    [Fact]
    public void AisGap_StationaryVessel_NotFlagged()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _sut.CheckPositionJump(1, 57.0, 11.0, 0.5, t); // stationary

        _sut.CheckAisGap(1, t.AddMinutes(15)).Should().BeNull();
    }

    [Fact]
    public void AisGap_UnknownVessel_NotFlagged()
    {
        _sut.CheckAisGap(99999, DateTime.UtcNow).Should().BeNull();
    }
}
