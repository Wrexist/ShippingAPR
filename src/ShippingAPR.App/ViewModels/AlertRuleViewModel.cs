using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class AlertRuleViewModel : ObservableObject, IDisposable
{
    private readonly AlertEngine _alertEngine;
    private readonly DispatcherTimer _refreshTimer;

    // New rule form fields
    [ObservableProperty]
    private string _newRuleName = "";

    [ObservableProperty]
    private VesselType? _newRuleVesselType;

    [ObservableProperty]
    private string _newRuleMinSpeed = "";

    [ObservableProperty]
    private string _newRuleMaxSpeed = "";

    [ObservableProperty]
    private string _newRuleFlag = "";

    [ObservableProperty]
    private int _ruleCount;

    [ObservableProperty]
    private int _alertsFiredCount;

    public ObservableCollection<AlertRuleItemViewModel> Rules { get; } = [];

    public ObservableCollection<string> VesselTypeOptions { get; } = [
        "", "Cargo", "Tanker", "Passenger", "Fishing", "Tug", "Pilot",
        "HighSpeedCraft", "Military", "Sailing", "PleasureCraft"
    ];

    public AlertRuleViewModel(AlertEngine alertEngine)
    {
        _alertEngine = alertEngine;

        _alertEngine.AlertFired += (_, alert) =>
        {
            _alertsFiredCount++;
            OnPropertyChanged(nameof(AlertsFiredCount));
        };

        _alertEngine.RulesChanged += (_, _) => RefreshRules();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _refreshTimer.Tick += (_, _) => RefreshRules();
        _refreshTimer.Start();

        RefreshRules();
    }

    private bool CanAddRule => !string.IsNullOrWhiteSpace(NewRuleName);

    partial void OnNewRuleNameChanged(string value) => AddRuleCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanAddRule))]
    private void AddRule()
    {
        if (string.IsNullOrWhiteSpace(NewRuleName)) return;

        var rule = new AlertRule
        {
            Name = NewRuleName,
            VesselTypeFilter = NewRuleVesselType,
            MinSpeedKnots = double.TryParse(NewRuleMinSpeed, out var min) ? min : null,
            MaxSpeedKnots = double.TryParse(NewRuleMaxSpeed, out var max) ? max : null,
            FlagFilter = string.IsNullOrWhiteSpace(NewRuleFlag) ? null : NewRuleFlag.Trim().ToUpperInvariant()
        };

        _alertEngine.AddRule(rule);

        // Reset form
        NewRuleName = "";
        NewRuleVesselType = null;
        NewRuleMinSpeed = "";
        NewRuleMaxSpeed = "";
        NewRuleFlag = "";
    }

    [RelayCommand]
    private void RemoveRule(string ruleId)
    {
        _alertEngine.RemoveRule(ruleId);
    }

    [RelayCommand]
    private void ToggleRule(string ruleId)
    {
        _alertEngine.ToggleRule(ruleId);
    }

    private void RefreshRules()
    {
        var rules = _alertEngine.Rules;
        RuleCount = rules.Count;

        Rules.Clear();
        foreach (var rule in rules)
        {
            var conditions = new List<string>();
            if (rule.VesselTypeFilter.HasValue) conditions.Add($"Type: {rule.VesselTypeFilter}");
            if (rule.MinSpeedKnots.HasValue) conditions.Add($"Min: {rule.MinSpeedKnots}kn");
            if (rule.MaxSpeedKnots.HasValue) conditions.Add($"Max: {rule.MaxSpeedKnots}kn");
            if (rule.FlagFilter is not null) conditions.Add($"Flag: {rule.FlagFilter}");

            Rules.Add(new AlertRuleItemViewModel
            {
                Id = rule.Id,
                Name = rule.Name,
                IsEnabled = rule.IsEnabled,
                ConditionsText = conditions.Count > 0
                    ? string.Join(" | ", conditions)
                    : "All vessels"
            });
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}

public sealed class AlertRuleItemViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsEnabled { get; init; }
    public required string ConditionsText { get; init; }

    public string StatusText => IsEnabled ? "Active" : "Disabled";
    public string StatusColor => IsEnabled ? "#4CAF50" : "#9E9E9E";
}
