using FluentAssertions;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.Services.Tests;

public class EmissionsEstimatorServiceTests
{
    private readonly Mock<IVesselStore> _storeMock = new();
    private readonly EmissionsEstimatorService _service;

    public EmissionsEstimatorServiceTests()
    {
        _storeMock.Setup(s => s.Vessels)
            .Returns(new Dictionary<int, Vessel>());
        _service = new EmissionsEstimatorService(_storeMock.Object);
    }

    private static Vessel CreateVessel(
        int mmsi = 123456789,
        VesselType type = VesselType.Cargo,
        double speed = 14.0,
        int lengthA = 100, int lengthB = 100,
        int beamC = 16, int beamD = 16,
        double draught = 10.0)
    {
        var vessel = new Vessel(mmsi);
        vessel.Update(
            new VesselPosition
            {
                Latitude = 57.0,
                Longitude = 12.0,
                SpeedOverGround = speed,
                CourseOverGround = 180.0,
                TrueHeading = 180.0,
                Status = NavigationalStatus.UnderWayUsingEngine,
                Timestamp = DateTime.UtcNow
            },
            new VesselStaticData
            {
                Name = "Test Vessel",
                ShipType = type,
                DimensionA = lengthA,
                DimensionB = lengthB,
                DimensionC = beamC,
                DimensionD = beamD,
                Draught = draught,
                CountryCode = "SE"
            });
        return vessel;
    }

    [Fact]
    public void Estimate_WithValidCargoVessel_ReturnsEstimate()
    {
        var vessel = CreateVessel(type: VesselType.Cargo, speed: 14.0);

        var result = _service.Estimate(vessel);

        result.Should().NotBeNull();
        result!.Co2TonnesPerHour.Should().BeGreaterThan(0);
        result.FuelTonnesPerHour.Should().BeGreaterThan(0);
        result.SoxKgPerHour.Should().BeGreaterThan(0);
        result.NoxKgPerHour.Should().BeGreaterThan(0);
        result.CiiRating.Should().BeOneOf('A', 'B', 'C', 'D', 'E');
    }

    [Fact]
    public void Estimate_NoPosition_ReturnsNull()
    {
        var vessel = new Vessel(123456789);

        var result = _service.Estimate(vessel);

        result.Should().BeNull();
    }

    [Fact]
    public void Estimate_StationaryVessel_ReturnsNull()
    {
        var vessel = CreateVessel(speed: 0.1);

        var result = _service.Estimate(vessel);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(VesselType.Tanker)]
    [InlineData(VesselType.Passenger)]
    [InlineData(VesselType.Fishing)]
    [InlineData(VesselType.Tug)]
    [InlineData(VesselType.Military)]
    [InlineData(VesselType.HighSpeedCraft)]
    [InlineData(VesselType.Sailing)]
    public void Estimate_DifferentVesselTypes_ReturnsEstimate(VesselType type)
    {
        var vessel = CreateVessel(type: type, speed: 10.0);

        var result = _service.Estimate(vessel);

        result.Should().NotBeNull();
        result!.Co2TonnesPerHour.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Estimate_HigherSpeed_ProducesMoreEmissions()
    {
        var slowVessel = CreateVessel(speed: 8.0);
        var fastVessel = CreateVessel(mmsi: 987654321, speed: 18.0);

        var slowResult = _service.Estimate(slowVessel);
        var fastResult = _service.Estimate(fastVessel);

        slowResult.Should().NotBeNull();
        fastResult.Should().NotBeNull();
        fastResult!.Co2TonnesPerHour.Should().BeGreaterThan(slowResult!.Co2TonnesPerHour);
    }

    [Fact]
    public void Estimate_LargerVessel_ProducesMoreEmissions()
    {
        var smallVessel = CreateVessel(lengthA: 50, lengthB: 50, beamC: 8, beamD: 8);
        var largeVessel = CreateVessel(mmsi: 987654321, lengthA: 150, lengthB: 150, beamC: 25, beamD: 25);

        var smallResult = _service.Estimate(smallVessel);
        var largeResult = _service.Estimate(largeVessel);

        smallResult.Should().NotBeNull();
        largeResult.Should().NotBeNull();
        largeResult!.Co2TonnesPerHour.Should().BeGreaterThan(smallResult!.Co2TonnesPerHour);
    }

    [Fact]
    public void Estimate_UnknownType_UsesDefaultProfile()
    {
        var vessel = CreateVessel(type: VesselType.OtherType, speed: 12.0);

        var result = _service.Estimate(vessel);

        result.Should().NotBeNull();
        result!.Co2TonnesPerHour.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Estimate_CiiRating_CorrectRange()
    {
        var vessel = CreateVessel(type: VesselType.Cargo, speed: 14.0);

        var result = _service.Estimate(vessel);

        result.Should().NotBeNull();
        result!.CiiRating.Should().BeInRange('A', 'E');
    }

    [Fact]
    public void Estimate_EmissionFactorsRelationship_Co2GreaterThanFuel()
    {
        var vessel = CreateVessel(speed: 14.0);

        var result = _service.Estimate(vessel);

        result.Should().NotBeNull();
        // CO2 factor is ~3.1x fuel
        result!.Co2TonnesPerHour.Should().BeGreaterThan(result.FuelTonnesPerHour);
    }

    [Fact]
    public void GetFleetSummary_EmptyStore_ReturnsZeroSummary()
    {
        _storeMock.Setup(s => s.Vessels)
            .Returns(new Dictionary<int, Vessel>());

        var summary = _service.GetFleetSummary();

        summary.TotalCo2TonnesPerHour.Should().Be(0);
        summary.VesselsWithEstimates.Should().Be(0);
        summary.CiiDistribution.Should().ContainKey('A');
        summary.CiiDistribution.Should().ContainKey('E');
    }

    [Fact]
    public void GetFleetSummary_WithVessels_ReturnsSummary()
    {
        var vessel1 = CreateVessel(mmsi: 100000001, speed: 14.0);
        var vessel2 = CreateVessel(mmsi: 100000002, type: VesselType.Tanker, speed: 12.0);
        var vessels = new Dictionary<int, Vessel>
        {
            [100000001] = vessel1,
            [100000002] = vessel2
        };
        _storeMock.Setup(s => s.Vessels).Returns(vessels);

        var summary = _service.GetFleetSummary();

        summary.VesselsWithEstimates.Should().Be(2);
        summary.TotalCo2TonnesPerHour.Should().BeGreaterThan(0);
        summary.TotalFuelTonnesPerHour.Should().BeGreaterThan(0);
        summary.HighestEmitterName.Should().NotBeEmpty();
        summary.CleanestVesselName.Should().NotBeEmpty();
        summary.AverageCo2PerVessel.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetFleetSummary_MixedVessels_CorrectCiiDistribution()
    {
        var vessel1 = CreateVessel(mmsi: 100000001, speed: 14.0);
        var vessel2 = CreateVessel(mmsi: 100000002, speed: 10.0);
        var vessels = new Dictionary<int, Vessel>
        {
            [100000001] = vessel1,
            [100000002] = vessel2
        };
        _storeMock.Setup(s => s.Vessels).Returns(vessels);

        var summary = _service.GetFleetSummary();

        summary.CiiDistribution.Values.Sum().Should().Be(2);
    }

    [Fact]
    public void EstimateFuelConsumption_ZeroSpeed_ReturnsZero()
    {
        var vessel = CreateVessel(speed: 0);
        var profile = new EmissionProfile(VesselType.Cargo, 1.8, 14.0, 200.0);

        var result = EmissionsEstimatorService.EstimateFuelConsumption(vessel, 0, profile);

        result.Should().Be(0);
    }

    [Fact]
    public void EstimateFuelConsumption_ReferenceSpeed_ReturnsReferenceFuel()
    {
        // Vessel with same length as reference → size factor = 1.0
        var vessel = CreateVessel(lengthA: 100, lengthB: 100); // LOA = 200
        var profile = new EmissionProfile(VesselType.Cargo, 1.8, 14.0, 200.0);

        var result = EmissionsEstimatorService.EstimateFuelConsumption(vessel, 14.0, profile);

        result.Should().BeApproximately(1.8, 0.01);
    }

    [Fact]
    public void EstimateFuelConsumption_NoDimensions_UsesSizeFactorOne()
    {
        var vessel = new Vessel(123456789);
        vessel.Update(
            new VesselPosition
            {
                SpeedOverGround = 14.0,
                Timestamp = DateTime.UtcNow
            },
            null);
        var profile = new EmissionProfile(VesselType.Cargo, 1.8, 14.0, 200.0);

        var result = EmissionsEstimatorService.EstimateFuelConsumption(vessel, 14.0, profile);

        result.Should().BeApproximately(1.8, 0.01);
    }

    [Fact]
    public void CalculateCiiRating_ReturnsValidRating()
    {
        var vessel = CreateVessel(speed: 14.0);

        var rating = EmissionsEstimatorService.CalculateCiiRating(vessel, 1.8);

        rating.Should().BeInRange('A', 'E');
    }
}
