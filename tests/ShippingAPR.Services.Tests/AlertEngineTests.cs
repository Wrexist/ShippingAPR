using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;
using Xunit;

namespace ShippingAPR.Services.Tests;

public class AlertEngineTests : IDisposable
{
    private readonly VesselStore _store = new();
    private readonly NotificationService _notificationService;
    private readonly AlertEngine _sut;

    public AlertEngineTests()
    {
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShippingAPR", "alert-rules.json");
        if (File.Exists(dataPath)) File.Delete(dataPath);

        var areaMonitor = new AreaMonitorService(_store, NullLogger<AreaMonitorService>.Instance);
        _notificationService = new NotificationService(
            areaMonitor,
            NullLogger<NotificationService>.Instance,
            Options.Create(new TrackingOptions()));
        _sut = new AlertEngine(
            _store,
            _notificationService,
            NullLogger<AlertEngine>.Instance);
    }

    [Fact]
    public void AddRule_AppearsInRulesList()
    {
        var rule = new AlertRule { Name = "Fast ships" , MinSpeedKnots = 20 };
        _sut.AddRule(rule);

        _sut.Rules.Should().ContainSingle(r => r.Name == "Fast ships");
    }

    [Fact]
    public void RemoveRule_RemovesFromList()
    {
        var rule = new AlertRule { Name = "Test rule" };
        _sut.AddRule(rule);
        _sut.RemoveRule(rule.Id);

        _sut.Rules.Should().BeEmpty();
    }

    [Fact]
    public void ToggleRule_DisablesAndReenables()
    {
        var rule = new AlertRule { Name = "Toggle test" };
        _sut.AddRule(rule);

        _sut.ToggleRule(rule.Id);
        _sut.Rules.Single().IsEnabled.Should().BeFalse();

        _sut.ToggleRule(rule.Id);
        _sut.Rules.Single().IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void MatchingVessel_FiresAlertEvent()
    {
        AlertTriggered? firedAlert = null;
        _sut.AlertFired += (_, a) => firedAlert = a;

        var rule = new AlertRule { Name = "Cargo alert", VesselTypeFilter = VesselType.Cargo };
        _sut.AddRule(rule);

        _store.AddOrUpdate(200000001, CreatePosition(10), new VesselStaticData { ShipType = VesselType.Cargo });

        firedAlert.Should().NotBeNull();
        firedAlert!.Rule.Name.Should().Be("Cargo alert");
    }

    [Fact]
    public void NonMatchingVessel_DoesNotFireAlert()
    {
        AlertTriggered? firedAlert = null;
        _sut.AlertFired += (_, a) => firedAlert = a;

        var rule = new AlertRule { Name = "Tanker alert", VesselTypeFilter = VesselType.Tanker };
        _sut.AddRule(rule);

        _store.AddOrUpdate(200000001, CreatePosition(10), new VesselStaticData { ShipType = VesselType.Cargo });

        firedAlert.Should().BeNull();
    }

    [Fact]
    public void DisabledRule_DoesNotFire()
    {
        AlertTriggered? firedAlert = null;
        _sut.AlertFired += (_, a) => firedAlert = a;

        var rule = new AlertRule { Name = "Disabled", VesselTypeFilter = VesselType.Cargo };
        _sut.AddRule(rule);
        _sut.ToggleRule(rule.Id);

        _store.AddOrUpdate(200000001, CreatePosition(10), new VesselStaticData { ShipType = VesselType.Cargo });

        firedAlert.Should().BeNull();
    }

    [Fact]
    public void Cooldown_PreventsRepeatedAlerts()
    {
        var fireCount = 0;
        _sut.AlertFired += (_, _) => fireCount++;

        var rule = new AlertRule { Name = "Speed alert", MinSpeedKnots = 5 };
        _sut.AddRule(rule);

        _store.AddOrUpdate(200000001, CreatePosition(10), null);
        _store.AddOrUpdate(200000001, CreatePosition(12), null);

        fireCount.Should().Be(1, "cooldown should prevent second alert");
    }

    [Fact]
    public void RulesChanged_EventFired_OnAddAndRemove()
    {
        var changedCount = 0;
        _sut.RulesChanged += (_, _) => changedCount++;

        var rule = new AlertRule { Name = "Test" };
        _sut.AddRule(rule);
        _sut.RemoveRule(rule.Id);

        changedCount.Should().Be(2);
    }

    [Fact]
    public void MatchingVessel_PublishesNotification()
    {
        var rule = new AlertRule { Name = "Notify test", MinSpeedKnots = 5 };
        _sut.AddRule(rule);

        _store.AddOrUpdate(200000001, CreatePosition(10), null);

        _notificationService.History.Should().Contain(n => n.Title.Contains("Notify test"));
    }

    private static VesselPosition CreatePosition(double speed = 5.0)
    {
        return new VesselPosition
        {
            Latitude = 57.7,
            Longitude = 11.9,
            SpeedOverGround = speed,
            CourseOverGround = 180,
            TrueHeading = 180,
            Status = NavigationalStatus.UnderWayUsingEngine
        };
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
