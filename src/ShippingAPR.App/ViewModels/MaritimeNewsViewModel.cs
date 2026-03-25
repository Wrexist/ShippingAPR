using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShippingAPR.Core.Models;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class MaritimeNewsViewModel : ObservableObject, IDisposable
{
    private readonly MaritimeIncidentService _incidentService;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty] private int _activeIncidentCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private string _selectedCategory = "All";

    public ObservableCollection<IncidentItemViewModel> Incidents { get; } = [];

    public MaritimeNewsViewModel(MaritimeIncidentService incidentService)
    {
        _incidentService = incidentService;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    partial void OnSelectedCategoryChanged(string value) => Refresh();

    [RelayCommand]
    private void FilterCategory(string? category)
    {
        SelectedCategory = category ?? "All";
    }

    private void Refresh()
    {
        var all = _incidentService.GetActive();
        ActiveIncidentCount = all.Count;
        WarningCount = all.Count(i => i.Severity >= IncidentSeverity.Warning);
        CriticalCount = all.Count(i => i.Severity == IncidentSeverity.Critical);

        var filtered = SelectedCategory == "All"
            ? all
            : all.Where(i => i.Category.ToString() == SelectedCategory).ToList();

        Incidents.Clear();
        foreach (var incident in filtered)
            Incidents.Add(new IncidentItemViewModel(incident));
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}

public partial class IncidentItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string CategoryText { get; }
    public string SeverityText { get; }
    public string SeverityColor { get; }
    public string SourceText { get; }
    public string TimeText { get; }
    public double Latitude { get; }
    public double Longitude { get; }

    public IncidentItemViewModel(MaritimeIncident incident)
    {
        Id = incident.Id;
        Title = incident.Title;
        Description = incident.Description;
        CategoryText = incident.Category.ToString();
        SeverityText = incident.Severity.ToString();
        SeverityColor = incident.Severity switch
        {
            IncidentSeverity.Info => "#60A5FA",
            IncidentSeverity.Advisory => "#FBBF24",
            IncidentSeverity.Warning => "#FB923C",
            IncidentSeverity.Critical => "#EF4444",
            _ => "#9E9E9E"
        };
        SourceText = incident.Source;
        Latitude = incident.Latitude;
        Longitude = incident.Longitude;

        var age = DateTime.UtcNow - incident.Timestamp;
        TimeText = age.TotalDays >= 1 ? $"{(int)age.TotalDays}d ago"
            : age.TotalHours >= 1 ? $"{(int)age.TotalHours}h ago"
            : "Just now";
    }
}
