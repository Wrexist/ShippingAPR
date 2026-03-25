using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class AchievementViewModel : ObservableObject, IDisposable
{
    private readonly AchievementService _achievementService;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty]
    private int _totalUnlocked;

    [ObservableProperty]
    private int _totalAchievements;

    [ObservableProperty]
    private int _uniqueVessels;

    [ObservableProperty]
    private int _typesDiscovered;

    [ObservableProperty]
    private int _countriesTracked;

    [ObservableProperty]
    private string _fastestSpeedText = "0 kn";

    [ObservableProperty]
    private double _progressPercent;

    public ObservableCollection<AchievementItemViewModel> Achievements { get; } = [];

    public AchievementViewModel(AchievementService achievementService)
    {
        _achievementService = achievementService;
        TotalAchievements = achievementService.Definitions.Count;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        WeakReferenceMessenger.Default.Register<AchievementUnlocked>(this, (_, msg) =>
        {
            Refresh();
        });

        BuildAchievementList();
        Refresh();
    }

    private void BuildAchievementList()
    {
        Achievements.Clear();
        foreach (var def in _achievementService.Definitions)
        {
            Achievements.Add(new AchievementItemViewModel
            {
                Id = def.Id,
                Name = def.Name,
                Description = def.Description,
                Icon = def.Icon,
                Target = def.Target,
                Tier = def.Tier,
                Category = def.Category
            });
        }
    }

    private void Refresh()
    {
        var progress = _achievementService.Progress;
        TotalUnlocked = _achievementService.TotalUnlocked;
        UniqueVessels = _achievementService.UniqueVesselsSpotted;
        TypesDiscovered = _achievementService.TypesDiscovered;
        CountriesTracked = _achievementService.CountriesTracked;
        FastestSpeedText = $"{_achievementService.FastestSpeedSeen:F1} kn";
        ProgressPercent = TotalAchievements > 0
            ? (double)TotalUnlocked / TotalAchievements * 100
            : 0;

        foreach (var item in Achievements)
        {
            if (progress.TryGetValue(item.Id, out var p))
            {
                item.CurrentValue = p.CurrentValue;
                item.IsUnlocked = p.IsUnlocked;
                item.UnlockedAt = p.UnlockedAt;
            }
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        WeakReferenceMessenger.Default.Unregister<AchievementUnlocked>(this);
    }
}

public partial class AchievementItemViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required int Target { get; init; }
    public required AchievementTier Tier { get; init; }
    public required AchievementCategory Category { get; init; }

    [ObservableProperty]
    private int _currentValue;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private DateTime? _unlockedAt;

    public double ProgressPercent => Target > 0 ? Math.Min(100, (double)CurrentValue / Target * 100) : 0;

    public string ProgressText => IsUnlocked ? "Complete!" : $"{CurrentValue}/{Target}";

    public string TierColor => Tier switch
    {
        AchievementTier.Bronze => "#CD7F32",
        AchievementTier.Silver => "#C0C0C0",
        AchievementTier.Gold => "#FFD700",
        AchievementTier.Platinum => "#E5E4E2",
        _ => "#808080"
    };
}
