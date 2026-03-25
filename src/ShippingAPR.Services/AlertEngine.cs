using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class AlertTriggered
{
    public required AlertRule Rule { get; init; }
    public required Vessel Vessel { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed class AlertEngine : IDisposable
{
    private static readonly string RulesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR",
        "alert-rules.json");

    private readonly IVesselStore _vesselStore;
    private readonly NotificationService _notificationService;
    private readonly ILogger<AlertEngine> _logger;
    private readonly List<AlertRule> _rules = [];
    private readonly object _rulesLock = new();

    // Track which rules have fired for which vessels (prevent spam)
    private readonly ConcurrentDictionary<string, DateTime> _firedRecently = new();
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);

    public event EventHandler<AlertTriggered>? AlertFired;
    public event EventHandler? RulesChanged;

    public IReadOnlyList<AlertRule> Rules
    {
        get { lock (_rulesLock) return _rules.ToList(); }
    }

    public AlertEngine(
        IVesselStore vesselStore,
        NotificationService notificationService,
        ILogger<AlertEngine> logger)
    {
        _vesselStore = vesselStore;
        _notificationService = notificationService;
        _logger = logger;

        Load();

        _vesselStore.VesselAdded += OnVesselUpdate;
        _vesselStore.VesselUpdated += OnVesselUpdate;
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        List<AlertRule> rules;
        lock (_rulesLock) { rules = _rules.ToList(); }

        foreach (var rule in rules)
        {
            if (!rule.Matches(vessel)) continue;

            var key = $"{rule.Id}:{vessel.Mmsi}";
            var now = DateTime.UtcNow;

            // Check cooldown
            if (_firedRecently.TryGetValue(key, out var lastFired) &&
                now - lastFired < CooldownPeriod)
                continue;

            _firedRecently[key] = now;

            var alert = new AlertTriggered
            {
                Rule = rule,
                Vessel = vessel,
                Timestamp = now
            };

            _notificationService.Publish(new NotificationMessage
            {
                Title = $"Alert: {rule.Name}",
                Body = $"{vessel.DisplayName} matched rule \"{rule.Name}\"" +
                       (vessel.CurrentPosition is { } pos ? $" at {pos.SpeedOverGround:F1} kn" : ""),
                Type = NotificationType.Warning
            });

            AlertFired?.Invoke(this, alert);
            _logger.LogInformation("Alert fired: {Rule} for {Vessel}", rule.Name, vessel.DisplayName);
        }

        // Cleanup expired cooldowns when dictionary exceeds a reasonable size
        if (_firedRecently.Count > 100)
        {
            var cutoff = DateTime.UtcNow - CooldownPeriod;
            var expired = _firedRecently
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var k in expired)
                _firedRecently.TryRemove(k, out _);
        }
    }

    public void AddRule(AlertRule rule)
    {
        lock (_rulesLock)
        {
            _rules.Add(rule);
        }
        Save();
        RulesChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Alert rule added: {Name}", rule.Name);
    }

    public void RemoveRule(string ruleId)
    {
        lock (_rulesLock)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
        }
        Save();
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleRule(string ruleId)
    {
        lock (_rulesLock)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is not null)
                rule.IsEnabled = !rule.IsEnabled;
        }
        Save();
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(RulesPath)) return;
            var json = File.ReadAllText(RulesPath);
            var rules = JsonSerializer.Deserialize<List<AlertRule>>(json);
            if (rules is null) return;

            lock (_rulesLock)
            {
                _rules.AddRange(rules);
            }
            _logger.LogDebug("Loaded {Count} alert rules", rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load alert rules");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(RulesPath)!;
            Directory.CreateDirectory(dir);

            List<AlertRule> rules;
            lock (_rulesLock) { rules = _rules.ToList(); }

            var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RulesPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save alert rules");
        }
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselUpdate;
        _vesselStore.VesselUpdated -= OnVesselUpdate;
        Save();
    }
}
