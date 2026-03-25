using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class EncounterJournalViewModel : ObservableObject, IDisposable
{
    private readonly EncounterJournalService _journalService;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty] private int _totalEncounters;
    [ObservableProperty] private int _uniqueCountries;
    [ObservableProperty] private int _uniqueTypes;
    [ObservableProperty] private string _rarestTypeText = "";
    [ObservableProperty] private string _searchQuery = "";

    public ObservableCollection<EncounterItemViewModel> Encounters { get; } = [];

    public EncounterJournalViewModel(EncounterJournalService journalService)
    {
        _journalService = journalService;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    partial void OnSearchQueryChanged(string value) => Refresh();

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = "";
    }

    private void Refresh()
    {
        var stats = _journalService.GetStats();
        TotalEncounters = stats.TotalEncounters;
        UniqueCountries = stats.UniqueCountries;
        UniqueTypes = stats.UniqueTypes;
        RarestTypeText = stats.RarestTypeName ?? "";

        var encounters = string.IsNullOrWhiteSpace(SearchQuery)
            ? _journalService.GetAll().TakeLast(100).Reverse().ToList()
            : _journalService.Search(SearchQuery, 100).ToList();

        // Simple full refresh for now (encounters are append-only)
        Encounters.Clear();
        foreach (var e in encounters)
            Encounters.Add(new EncounterItemViewModel(e));
    }

    [RelayCommand]
    private void ExportJournal()
    {
        var csv = _journalService.ExportToCsv();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"ship_journal_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dialog.ShowDialog() == true)
            System.IO.File.WriteAllText(dialog.FileName, csv);
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}

public partial class EncounterItemViewModel : ObservableObject
{
    public string Id { get; }
    public string VesselName { get; }
    public string TypeText { get; }
    public string CountryText { get; }
    public string SpeedText { get; }
    public string TimeText { get; }
    public string DestinationText { get; }
    public int Rating { get; }
    public string RatingText { get; }

    public EncounterItemViewModel(Encounter encounter)
    {
        Id = encounter.Id;
        VesselName = encounter.VesselName;
        TypeText = encounter.VesselType.ToString();
        CountryText = encounter.CountryCode ?? "??";
        SpeedText = $"{encounter.SpeedKnots:F1} kn";
        DestinationText = encounter.Destination ?? "Unknown";
        Rating = encounter.Rating;
        RatingText = encounter.Rating > 0 ? new string('\u2605', encounter.Rating) : "";

        var age = DateTime.UtcNow - encounter.Timestamp;
        TimeText = age.TotalDays >= 1 ? $"{(int)age.TotalDays}d ago"
            : age.TotalHours >= 1 ? $"{(int)age.TotalHours}h ago"
            : $"{(int)age.TotalMinutes}m ago";
    }
}
