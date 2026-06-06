using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using ShippingAPR.App.Configuration;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly IVesselStore _vesselStore;
    private readonly int _maxResults;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _showResults;

    [ObservableProperty]
    private bool _hasNoResults;

    public ObservableCollection<Vessel> Results { get; } = [];

    public event EventHandler<Vessel>? VesselSelected;
    public event EventHandler? FocusRequested;

    [RelayCommand]
    private void FocusSearch()
    {
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public SearchViewModel(IVesselStore vesselStore, IOptions<UiOptions> uiOptions)
    {
        _vesselStore = vesselStore;
        _maxResults = uiOptions.Value.SearchMaxResults;
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Results.Clear();
            ShowResults = false;
            return;
        }

        PerformSearch(value);
    }

    [RelayCommand]
    private void PerformSearch(string query)
    {
        Results.Clear();

        var results = (_vesselStore.Search(query) ?? Enumerable.Empty<Vessel>()).Take(_maxResults);
        foreach (var vessel in results)
            Results.Add(vessel);

        HasNoResults = Results.Count == 0 && !string.IsNullOrWhiteSpace(query);
        ShowResults = Results.Count > 0 || HasNoResults;
    }

    [RelayCommand]
    private void SelectResult(Vessel? vessel)
    {
        if (vessel is null) return;

        VesselSelected?.Invoke(this, vessel);
        // Set the query first (it re-triggers a search), then force the dropdown closed,
        // so selecting a result doesn't immediately re-open it with a "no results" message.
        SearchQuery = vessel.DisplayName;
        ShowResults = false;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        Results.Clear();
        ShowResults = false;
    }
}
