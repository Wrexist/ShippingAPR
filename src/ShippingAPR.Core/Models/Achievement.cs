namespace ShippingAPR.Core.Models;

public sealed class AchievementDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required AchievementCategory Category { get; init; }
    public required int Target { get; init; }
    public AchievementTier Tier { get; init; } = AchievementTier.Bronze;
}

public sealed class AchievementProgress
{
    public required string AchievementId { get; init; }
    public int CurrentValue { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
}

public enum AchievementCategory
{
    VesselsSpotted,
    TypesDiscovered,
    CountriesTracked,
    SpeedRecords,
    RareFinds,
    Milestones,
    WeatherEvents,
    TimeTracking
}

public enum AchievementTier
{
    Bronze,
    Silver,
    Gold,
    Platinum,
    Legendary
}
