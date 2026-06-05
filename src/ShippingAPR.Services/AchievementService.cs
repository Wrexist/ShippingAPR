using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

using ShippingAPR.Core.IO;

namespace ShippingAPR.Services;

public sealed class AchievementUnlocked : ValueChangedMessage<AchievementDefinition>
{
    public AchievementUnlocked(AchievementDefinition value) : base(value) { }
}

public sealed class AchievementService : IDisposable
{
    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR",
        "achievements.json");

    private readonly IVesselStore _vesselStore;
    private readonly NotificationService _notificationService;
    private readonly ILogger<AchievementService> _logger;
    private readonly object _lock = new();

    // Spotter stats
    private readonly HashSet<int> _uniqueMmsis = [];
    private readonly HashSet<VesselType> _typesDiscovered = [];
    private readonly HashSet<string> _countriesTracked = [];
    private double _fastestSpeedSeen;
    private int _largestShipLength;
    private readonly HashSet<VesselType> _rareTypes = [
        VesselType.Military, VesselType.SearchAndRescue, VesselType.MedicalTransport,
        VesselType.Diving, VesselType.WingInGround
    ];
    private int _rareSpotCount;

    private readonly List<AchievementDefinition> _definitions;
    private readonly ConcurrentDictionary<string, AchievementProgress> _progress = new();

    public IReadOnlyList<AchievementDefinition> Definitions => _definitions;

    public IReadOnlyDictionary<string, AchievementProgress> Progress =>
        _progress.ToDictionary(k => k.Key, v => v.Value);

    public int TotalUnlocked => _progress.Values.Count(p => p.IsUnlocked);
    public int UniqueVesselsSpotted { get { lock (_lock) return _uniqueMmsis.Count; } }
    public int TypesDiscovered { get { lock (_lock) return _typesDiscovered.Count; } }
    public int CountriesTracked { get { lock (_lock) return _countriesTracked.Count; } }
    public double FastestSpeedSeen { get { lock (_lock) return _fastestSpeedSeen; } }

    public AchievementService(
        IVesselStore vesselStore,
        NotificationService notificationService,
        ILogger<AchievementService> logger)
    {
        _vesselStore = vesselStore;
        _notificationService = notificationService;
        _logger = logger;

        _definitions = BuildDefinitions();
        Load();

        _vesselStore.VesselAdded += OnVesselUpdate;
        _vesselStore.VesselUpdated += OnVesselUpdate;
    }

    private void OnVesselUpdate(object? sender, Vessel vessel)
    {
        lock (_lock)
        {
            var isNew = _uniqueMmsis.Add(vessel.Mmsi);

            if (vessel.Type != VesselType.Unknown)
                _typesDiscovered.Add(vessel.Type);

            if (vessel.StaticData?.CountryCode is { Length: > 0 } cc)
                _countriesTracked.Add(cc);

            if (vessel.CurrentPosition is { SpeedOverGround: > 0 } pos)
            {
                if (pos.SpeedOverGround > _fastestSpeedSeen)
                    _fastestSpeedSeen = pos.SpeedOverGround;
            }

            if (vessel.StaticData is { LengthOverall: > 0 } sd)
            {
                if (sd.LengthOverall > _largestShipLength)
                    _largestShipLength = sd.LengthOverall;
            }

            if (_rareTypes.Contains(vessel.Type) && isNew)
                _rareSpotCount++;

            CheckAchievements();
        }
    }

    private void CheckAchievements()
    {
        foreach (var def in _definitions)
        {
            var progress = _progress.GetOrAdd(def.Id, _ => new AchievementProgress
            {
                AchievementId = def.Id
            });

            if (progress.IsUnlocked) continue;

            var currentValue = GetCurrentValue(def);
            progress.CurrentValue = currentValue;

            if (currentValue >= def.Target)
            {
                progress.IsUnlocked = true;
                progress.UnlockedAt = DateTime.UtcNow;

                _notificationService.Publish(new NotificationMessage
                {
                    Title = $"Achievement Unlocked: {def.Name}",
                    Body = $"{def.Icon} {def.Description}",
                    Type = NotificationType.Info
                });

                WeakReferenceMessenger.Default.Send(new AchievementUnlocked(def));
                _logger.LogInformation("Achievement unlocked: {Name}", def.Name);
            }
        }

        Save();
    }

    private int GetCurrentValue(AchievementDefinition def) => def.Category switch
    {
        AchievementCategory.VesselsSpotted => _uniqueMmsis.Count,
        AchievementCategory.TypesDiscovered => _typesDiscovered.Count,
        AchievementCategory.CountriesTracked => _countriesTracked.Count,
        AchievementCategory.SpeedRecords => (int)_fastestSpeedSeen,
        AchievementCategory.RareFinds => _rareSpotCount,
        AchievementCategory.Milestones => _largestShipLength,
        _ => 0
    };

    private static List<AchievementDefinition> BuildDefinitions() =>
    [
        // Vessels Spotted milestones
        new() { Id = "spot_10", Name = "Deck Cadet", Description = "Spot 10 unique vessels", Icon = "\u26F5", Category = AchievementCategory.VesselsSpotted, Target = 10, Tier = AchievementTier.Bronze },
        new() { Id = "spot_50", Name = "Able Seaman", Description = "Spot 50 unique vessels", Icon = "\U0001F6A2", Category = AchievementCategory.VesselsSpotted, Target = 50, Tier = AchievementTier.Silver },
        new() { Id = "spot_200", Name = "First Mate", Description = "Spot 200 unique vessels", Icon = "\U0001F6F3", Category = AchievementCategory.VesselsSpotted, Target = 200, Tier = AchievementTier.Gold },
        new() { Id = "spot_500", Name = "Ship Captain", Description = "Spot 500 unique vessels", Icon = "\U0001F451", Category = AchievementCategory.VesselsSpotted, Target = 500, Tier = AchievementTier.Gold },
        new() { Id = "spot_1000", Name = "Fleet Admiral", Description = "Spot 1000 unique vessels", Icon = "\u2B50", Category = AchievementCategory.VesselsSpotted, Target = 1000, Tier = AchievementTier.Platinum },

        // Types Discovered
        new() { Id = "types_3", Name = "Type Trainee", Description = "Discover 3 vessel types", Icon = "\U0001F4D6", Category = AchievementCategory.TypesDiscovered, Target = 3, Tier = AchievementTier.Bronze },
        new() { Id = "types_8", Name = "Type Expert", Description = "Discover 8 vessel types", Icon = "\U0001F393", Category = AchievementCategory.TypesDiscovered, Target = 8, Tier = AchievementTier.Silver },
        new() { Id = "types_15", Name = "Type Encyclopedist", Description = "Discover 15 vessel types", Icon = "\U0001F3C6", Category = AchievementCategory.TypesDiscovered, Target = 15, Tier = AchievementTier.Gold },

        // Countries Tracked
        new() { Id = "flags_5", Name = "Port of Call", Description = "Track ships from 5 countries", Icon = "\U0001F3F3", Category = AchievementCategory.CountriesTracked, Target = 5, Tier = AchievementTier.Bronze },
        new() { Id = "flags_20", Name = "World Traveler", Description = "Track ships from 20 countries", Icon = "\U0001F30D", Category = AchievementCategory.CountriesTracked, Target = 20, Tier = AchievementTier.Silver },
        new() { Id = "flags_50", Name = "Globe Trotter", Description = "Track ships from 50 countries", Icon = "\U0001F30E", Category = AchievementCategory.CountriesTracked, Target = 50, Tier = AchievementTier.Gold },

        // Speed Records
        new() { Id = "speed_20", Name = "Fast Lane", Description = "Spot a vessel doing 20+ knots", Icon = "\U0001F4A8", Category = AchievementCategory.SpeedRecords, Target = 20, Tier = AchievementTier.Bronze },
        new() { Id = "speed_30", Name = "Speed Demon", Description = "Spot a vessel doing 30+ knots", Icon = "\u26A1", Category = AchievementCategory.SpeedRecords, Target = 30, Tier = AchievementTier.Silver },
        new() { Id = "speed_40", Name = "Hydrofoil Hunter", Description = "Spot a vessel doing 40+ knots", Icon = "\U0001F680", Category = AchievementCategory.SpeedRecords, Target = 40, Tier = AchievementTier.Gold },

        // Rare Finds
        new() { Id = "rare_1", Name = "Rare Catch", Description = "Spot a rare vessel type", Icon = "\U0001F48E", Category = AchievementCategory.RareFinds, Target = 1, Tier = AchievementTier.Silver },
        new() { Id = "rare_5", Name = "Collector", Description = "Spot 5 rare vessel types", Icon = "\U0001F3AF", Category = AchievementCategory.RareFinds, Target = 5, Tier = AchievementTier.Gold },

        // Ship Size milestones
        new() { Id = "size_200", Name = "Big Spotter", Description = "Spot a vessel over 200m long", Icon = "\U0001F4CF", Category = AchievementCategory.Milestones, Target = 200, Tier = AchievementTier.Bronze },
        new() { Id = "size_300", Name = "Giant Hunter", Description = "Spot a vessel over 300m long", Icon = "\U0001F3D7", Category = AchievementCategory.Milestones, Target = 300, Tier = AchievementTier.Silver },
        new() { Id = "size_400", Name = "Leviathan", Description = "Spot a vessel over 400m long", Icon = "\U0001F40B", Category = AchievementCategory.Milestones, Target = 400, Tier = AchievementTier.Gold },
    ];

    private void Load()
    {
        try
        {
            if (!File.Exists(DataPath)) return;

            var json = File.ReadAllText(DataPath);
            var data = JsonSerializer.Deserialize<AchievementSaveData>(json);
            if (data is null) return;

            foreach (var mmsi in data.UniqueMmsis)
                _uniqueMmsis.Add(mmsi);
            foreach (var type in data.TypesDiscovered)
                _typesDiscovered.Add(type);
            foreach (var cc in data.CountriesTracked)
                _countriesTracked.Add(cc);
            _fastestSpeedSeen = data.FastestSpeed;
            _largestShipLength = data.LargestShipLength;
            _rareSpotCount = data.RareSpotCount;

            foreach (var p in data.Achievements)
                _progress[p.AchievementId] = p;

            _logger.LogDebug("Loaded achievements: {Unlocked}/{Total} unlocked",
                TotalUnlocked, _definitions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load achievements");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(DataPath)!;
            Directory.CreateDirectory(dir);

            var data = new AchievementSaveData
            {
                UniqueMmsis = [.. _uniqueMmsis],
                TypesDiscovered = [.. _typesDiscovered],
                CountriesTracked = [.. _countriesTracked],
                FastestSpeed = _fastestSpeedSeen,
                LargestShipLength = _largestShipLength,
                RareSpotCount = _rareSpotCount,
                Achievements = [.. _progress.Values]
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(DataPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save achievements");
        }
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselUpdate;
        _vesselStore.VesselUpdated -= OnVesselUpdate;
        Save();
    }

    private sealed class AchievementSaveData
    {
        public List<int> UniqueMmsis { get; set; } = [];
        public List<VesselType> TypesDiscovered { get; set; } = [];
        public List<string> CountriesTracked { get; set; } = [];
        public double FastestSpeed { get; set; }
        public int LargestShipLength { get; set; }
        public int RareSpotCount { get; set; }
        public List<AchievementProgress> Achievements { get; set; } = [];
    }
}
