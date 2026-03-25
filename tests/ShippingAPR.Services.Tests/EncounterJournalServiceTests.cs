using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.Services.Tests;

public class EncounterJournalServiceTests : IDisposable
{
    private readonly Mock<IVesselStore> _storeMock = new();
    private readonly Mock<ILogger<EncounterJournalService>> _loggerMock = new();
    private readonly EncounterJournalService _service;

    public EncounterJournalServiceTests()
    {
        // Delete persisted data to prevent cross-test contamination
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShippingAPR", "encounter-journal.json");
        if (File.Exists(dataPath)) File.Delete(dataPath);

        _storeMock.Setup(s => s.Vessels).Returns(new Dictionary<int, Vessel>());
        _service = new EncounterJournalService(_storeMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void GetAll_EmptyJournal_ReturnsEmpty()
    {
        _service.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void VesselAdded_CreatesEncounter()
    {
        var vessel = CreateVessel(100000001);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);

        var encounters = _service.GetAll();
        encounters.Should().HaveCount(1);
        encounters[0].Mmsi.Should().Be(100000001);
        encounters[0].VesselName.Should().Be("Test Vessel");
    }

    [Fact]
    public void VesselAdded_SameMmsiTwice_OnlyOneEncounter()
    {
        var vessel = CreateVessel(100000001);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);
        _storeMock.Raise(s => s.VesselAdded += null!, null!, vessel);

        _service.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void VesselAdded_DifferentMmsi_TwoEncounters()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001));
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000002));

        _service.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void VesselAdded_FiresEncounterAddedEvent()
    {
        Encounter? firedEncounter = null;
        _service.EncounterAdded += (_, e) => firedEncounter = e;

        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001));

        firedEncounter.Should().NotBeNull();
        firedEncounter!.Mmsi.Should().Be(100000001);
    }

    [Fact]
    public void Search_ByName_ReturnsMatches()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001, "Alpha Ship"));
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000002, "Beta Carrier"));

        var results = _service.Search("Alpha");
        results.Should().HaveCount(1);
        results[0].VesselName.Should().Be("Alpha Ship");
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAll()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001));
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000002));

        _service.Search("").Should().HaveCount(2);
    }

    [Fact]
    public void UpdateRating_ValidEncounter_SetsRating()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001));
        var encounter = _service.GetAll()[0];

        _service.UpdateRating(encounter.Id, 4);

        _service.GetAll()[0].Rating.Should().Be(4);
    }

    [Fact]
    public void UpdateRating_ClampsTo5()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001));
        var encounter = _service.GetAll()[0];

        _service.UpdateRating(encounter.Id, 10);

        _service.GetAll()[0].Rating.Should().Be(5);
    }

    [Fact]
    public void UpdateNotes_ValidEncounter_SetsNotes()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001));
        var encounter = _service.GetAll()[0];

        _service.UpdateNotes(encounter.Id, "Beautiful ship!");

        _service.GetAll()[0].UserNotes.Should().Be("Beautiful ship!");
    }

    [Fact]
    public void GetStats_WithEncounters_ReturnsCorrectStats()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001, countryCode: "SE"));
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000002, countryCode: "DK"));

        var stats = _service.GetStats();
        stats.TotalEncounters.Should().Be(2);
        stats.UniqueCountries.Should().Be(2);
    }

    [Fact]
    public void GetStats_EmptyJournal_ReturnsZeros()
    {
        var stats = _service.GetStats();
        stats.TotalEncounters.Should().Be(0);
        stats.UniqueCountries.Should().Be(0);
    }

    [Fact]
    public void ExportToCsv_ReturnsValidCsv()
    {
        _storeMock.Raise(s => s.VesselAdded += null!, null!, CreateVessel(100000001));

        var csv = _service.ExportToCsv();
        csv.Should().Contain("MMSI");
        csv.Should().Contain("100000001");
        csv.Should().Contain("Test Vessel");
    }

    private static Vessel CreateVessel(int mmsi, string name = "Test Vessel",
        string countryCode = "SE")
    {
        var vessel = new Vessel { Mmsi = mmsi };
        vessel.UpdatePosition(
            new VesselPosition
            {
                Latitude = 57.0, Longitude = 12.0,
                SpeedOverGround = 12.0, Timestamp = DateTime.UtcNow
            });
        vessel.UpdateStaticData(
            new VesselStaticData
            {
                Name = name, ShipType = VesselType.Cargo,
                CountryCode = countryCode, Destination = "SEGOT"
            });
        return vessel;
    }

    public void Dispose() => _service.Dispose();
}
