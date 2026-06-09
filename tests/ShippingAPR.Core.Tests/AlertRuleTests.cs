using FluentAssertions;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class AlertRuleTests
{
    [Fact]
    public void Rule_MatchesAll_WhenNoFilters()
    {
        var rule = new AlertRule { Name = "All vessels" };
        var vessel = CreateVessel(1, VesselType.Cargo, "SE", 12.0);

        rule.Matches(vessel).Should().BeTrue();
    }

    [Fact]
    public void Rule_FiltersByType()
    {
        var rule = new AlertRule { Name = "Tankers only", VesselTypeFilter = VesselType.Tanker };

        var cargo = CreateVessel(1, VesselType.Cargo, "SE", 12.0);
        var tanker = CreateVessel(2, VesselType.Tanker, "NO", 12.0);

        rule.Matches(cargo).Should().BeFalse();
        rule.Matches(tanker).Should().BeTrue();
    }

    [Fact]
    public void Rule_FiltersByMinSpeed()
    {
        var rule = new AlertRule { Name = "Fast vessels", MinSpeedKnots = 15.0 };

        var slow = CreateVessel(1, VesselType.Cargo, "SE", 10.0);
        var fast = CreateVessel(2, VesselType.Cargo, "SE", 20.0);

        rule.Matches(slow).Should().BeFalse();
        rule.Matches(fast).Should().BeTrue();
    }

    [Fact]
    public void Rule_FiltersByMaxSpeed()
    {
        var rule = new AlertRule { Name = "Slow vessels", MaxSpeedKnots = 5.0 };

        var slow = CreateVessel(1, VesselType.Cargo, "SE", 3.0);
        var fast = CreateVessel(2, VesselType.Cargo, "SE", 10.0);

        rule.Matches(slow).Should().BeTrue();
        rule.Matches(fast).Should().BeFalse();
    }

    [Fact]
    public void Rule_FiltersByFlag()
    {
        var rule = new AlertRule { Name = "Swedish ships", FlagFilter = "SE" };

        var swedish = CreateVessel(1, VesselType.Cargo, "SE", 12.0);
        var norwegian = CreateVessel(2, VesselType.Cargo, "NO", 12.0);

        rule.Matches(swedish).Should().BeTrue();
        rule.Matches(norwegian).Should().BeFalse();
    }

    [Fact]
    public void Rule_FiltersByZone()
    {
        var zone = new AlertZone("Test Zone", 57.0, 58.0, 11.0, 12.0);
        var rule = new AlertRule { Name = "Zone watch", ZoneFilter = zone };

        var inside = CreateVessel(1, VesselType.Cargo, "SE", 12.0, 57.5, 11.5);
        var outside = CreateVessel(2, VesselType.Cargo, "SE", 12.0, 55.0, 13.0);

        rule.Matches(inside).Should().BeTrue();
        rule.Matches(outside).Should().BeFalse();
    }

    [Fact]
    public void Rule_CompoundFilters_RequireAllConditions()
    {
        var rule = new AlertRule
        {
            Name = "Fast Swedish tankers",
            VesselTypeFilter = VesselType.Tanker,
            MinSpeedKnots = 15.0,
            FlagFilter = "SE"
        };

        // Matches all conditions
        var match = CreateVessel(1, VesselType.Tanker, "SE", 20.0);
        rule.Matches(match).Should().BeTrue();

        // Wrong type
        var wrongType = CreateVessel(2, VesselType.Cargo, "SE", 20.0);
        rule.Matches(wrongType).Should().BeFalse();

        // Too slow
        var tooSlow = CreateVessel(3, VesselType.Tanker, "SE", 10.0);
        rule.Matches(tooSlow).Should().BeFalse();

        // Wrong flag
        var wrongFlag = CreateVessel(4, VesselType.Tanker, "NO", 20.0);
        rule.Matches(wrongFlag).Should().BeFalse();
    }

    [Fact]
    public void DisabledRule_NeverMatches()
    {
        var rule = new AlertRule { Name = "Disabled", IsEnabled = false };
        var vessel = CreateVessel(1, VesselType.Cargo, "SE", 12.0);

        rule.Matches(vessel).Should().BeFalse();
    }

    [Fact]
    public void Rule_FiltersByMmsiSet()
    {
        var rule = new AlertRule { Name = "Specific ships", MmsiFilter = new HashSet<int> { 100, 200, 300 } };

        var match = CreateVessel(200, VesselType.Cargo, "SE", 12.0);
        var noMatch = CreateVessel(999, VesselType.Cargo, "SE", 12.0);

        rule.Matches(match).Should().BeTrue();
        rule.Matches(noMatch).Should().BeFalse();
    }

    [Fact]
    public void SpeedFilter_WithNoPosition_ReturnsFalse()
    {
        var rule = new AlertRule { Name = "Fast vessels", MinSpeedKnots = 15.0 };

        var vessel = new Vessel { Mmsi = 100000001 };
        vessel.UpdateStaticData(new VesselStaticData { ShipType = VesselType.Cargo, CountryCode = "SE" });

        rule.Matches(vessel).Should().BeFalse();
    }

    [Fact]
    public void StatusFilter_WithNoPosition_ReturnsFalse()
    {
        var rule = new AlertRule { Name = "Anchored", StatusFilter = NavigationalStatus.AtAnchor };

        var vessel = new Vessel { Mmsi = 100000001 };
        vessel.UpdateStaticData(new VesselStaticData { ShipType = VesselType.Cargo });

        rule.Matches(vessel).Should().BeFalse();
    }

    [Fact]
    public void StaticOnlyFilter_WithNoPosition_StillMatches()
    {
        // A flag/type rule has no position dependency, so it should still match.
        var rule = new AlertRule { Name = "Swedish", FlagFilter = "SE" };

        var vessel = new Vessel { Mmsi = 100000001 };
        vessel.UpdateStaticData(new VesselStaticData { ShipType = VesselType.Cargo, CountryCode = "SE" });

        rule.Matches(vessel).Should().BeTrue();
    }

    [Fact]
    public void ZoneFilter_WithNoPosition_ReturnsFalse()
    {
        var zone = new AlertZone("Test Zone", 57.0, 58.0, 11.0, 12.0);
        var rule = new AlertRule { Name = "Zone watch", ZoneFilter = zone };

        // Vessel with no position data
        var vessel = new Vessel { Mmsi = 100000001 };
        vessel.UpdateStaticData(new VesselStaticData
        {
            ShipType = VesselType.Cargo,
            CountryCode = "SE"
        });

        rule.Matches(vessel).Should().BeFalse();
    }

    private static Vessel CreateVessel(int mmsi, VesselType type, string flag, double speed,
        double lat = 57.7, double lon = 11.9)
    {
        var vessel = new Vessel { Mmsi = mmsi };
        vessel.UpdatePosition(new VesselPosition
        {
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = speed,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = NavigationalStatus.UnderWayUsingEngine
        });
        vessel.UpdateStaticData(new VesselStaticData
        {
            ShipType = type,
            CountryCode = flag
        });
        return vessel;
    }
}
