using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class VoyageNarrativeServiceTests : IDisposable
{
    private readonly VesselStore _store = new();
    private readonly Mock<IPortRepository> _portRepo = new();
    private readonly VoyageNarrativeService _sut;

    public VoyageNarrativeServiceTests()
    {
        _portRepo.Setup(x => x.GetAll()).Returns(new List<Port>());
        _sut = new VoyageNarrativeService(
            _store,
            _portRepo.Object,
            NullLogger<VoyageNarrativeService>.Instance);
    }

    [Fact]
    public void NewVessel_GeneratesFirstSeenEvent()
    {
        var pos = new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
            SpeedOverGround = 12,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = NavigationalStatus.UnderWayUsingEngine
        };

        _store.AddOrUpdate(100000123, pos, new VesselStaticData
        {
            Name = "MV TestShip",
            ShipType = VesselType.Cargo
        });

        var events = _sut.GetEvents(100000123);
        events.Should().HaveCount(1);
        events[0].Type.Should().Be(VoyageEventType.FirstSeen);
        events[0].Description.Should().Contain("First detected");
    }

    [Fact]
    public void Narrative_IncludesVesselName()
    {
        _store.AddOrUpdate(100000123,
            new VesselPosition
            {
                Latitude = 57.7, Longitude = 11.9,
                SpeedOverGround = 12, CourseOverGround = 180,
                TrueHeading = 180, Status = NavigationalStatus.UnderWayUsingEngine
            },
            new VesselStaticData { Name = "MV TestShip", ShipType = VesselType.Cargo });

        var narrative = _sut.GenerateNarrative(100000123);
        narrative.Should().Contain("MV TestShip");
        narrative.Should().Contain("cargo vessel");
    }

    [Fact]
    public void SpeedChange_RecordsEvent()
    {
        var pos1 = new VesselPosition
        {
            Latitude = 57.7, Longitude = 11.9,
            SpeedOverGround = 5, CourseOverGround = 180,
            TrueHeading = 180, Status = NavigationalStatus.UnderWayUsingEngine
        };
        _store.AddOrUpdate(100000123, pos1, null);

        var pos2 = new VesselPosition
        {
            Latitude = 57.8, Longitude = 11.9,
            SpeedOverGround = 15, CourseOverGround = 180,
            TrueHeading = 180, Status = NavigationalStatus.UnderWayUsingEngine
        };
        _store.AddOrUpdate(100000123, pos2, null);

        var events = _sut.GetEvents(100000123);
        events.Should().Contain(e => e.Type == VoyageEventType.SpeedChange);
    }

    [Fact]
    public void StatusChange_RecordsEvent()
    {
        var pos1 = new VesselPosition
        {
            Latitude = 57.7, Longitude = 11.9,
            SpeedOverGround = 5, CourseOverGround = 180,
            TrueHeading = 180, Status = NavigationalStatus.UnderWayUsingEngine
        };
        _store.AddOrUpdate(100000123, pos1, null);

        var pos2 = new VesselPosition
        {
            Latitude = 57.7, Longitude = 11.9,
            SpeedOverGround = 0, CourseOverGround = 180,
            TrueHeading = 180, Status = NavigationalStatus.AtAnchor
        };
        _store.AddOrUpdate(100000123, pos2, null);

        var events = _sut.GetEvents(100000123);
        events.Should().Contain(e => e.Type == VoyageEventType.Anchored);
    }

    [Fact]
    public void NonExistentVessel_ReturnsNotFound()
    {
        var narrative = _sut.GenerateNarrative(100000999);
        narrative.Should().Be("Vessel not found.");
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
