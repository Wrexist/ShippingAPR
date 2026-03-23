using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IVesselStore _vesselStore;
    private readonly IVesselTrackingService _trackingService;

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private string _connectionStatusColor = "#FFFF3D71";

    [ObservableProperty]
    private int _vesselCount;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _currentLanguage = "en";

    [ObservableProperty]
    private string _notificationText = string.Empty;

    [ObservableProperty]
    private bool _showNotification;

    public MapViewModel MapViewModel { get; }
    public VesselListViewModel VesselListViewModel { get; }
    public VesselDetailViewModel VesselDetailViewModel { get; }
    public FilterViewModel FilterViewModel { get; }
    public SearchViewModel SearchViewModel { get; }

    public MainViewModel(
        IVesselStore vesselStore,
        IVesselTrackingService trackingService,
        MapViewModel mapViewModel,
        VesselListViewModel vesselListViewModel,
        VesselDetailViewModel vesselDetailViewModel,
        FilterViewModel filterViewModel,
        SearchViewModel searchViewModel)
    {
        _vesselStore = vesselStore;
        _trackingService = trackingService;

        MapViewModel = mapViewModel;
        VesselListViewModel = vesselListViewModel;
        VesselDetailViewModel = vesselDetailViewModel;
        FilterViewModel = filterViewModel;
        SearchViewModel = searchViewModel;

        _trackingService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _vesselStore.VesselAdded += (_, _) => VesselCount = _vesselStore.Count;
        _vesselStore.StoreCleared += (_, _) => VesselCount = 0;

        // Listen for notifications
        WeakReferenceMessenger.Default.Register<NotificationPublished>(this, (_, msg) =>
        {
            NotificationText = $"{msg.Value.Title}: {msg.Value.Body}";
            ShowNotification = true;

            // Auto-hide after 5 seconds
            Task.Delay(5000).ContinueWith(_ =>
            {
                Application.Current?.Dispatcher.Invoke(() => ShowNotification = false);
            });
        });

        // Wire vessel selection from list to detail
        VesselListViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VesselListViewModel.SelectedVessel))
            {
                VesselDetailViewModel.Vessel = VesselListViewModel.SelectedVessel;
                MapViewModel.HighlightVessel(VesselListViewModel.SelectedVessel);
            }
        };

        // Wire search results
        SearchViewModel.VesselSelected += (_, vessel) =>
        {
            VesselDetailViewModel.Vessel = vessel;
            VesselListViewModel.SelectedVessel = vessel;
            MapViewModel.CenterOnVessel(vessel);
        };
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        var themeUri = IsDarkTheme
            ? "Assets/Themes/DarkTheme.xaml"
            : "Assets/Themes/LightTheme.xaml";

        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) });
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == "en" ? "sv" : "en";

        var culture = new CultureInfo(CurrentLanguage == "sv" ? "sv-SE" : "en-US");
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // Refresh bindings by notifying all properties
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void DismissNotification()
    {
        ShowNotification = false;
    }

    private void OnConnectionStatusChanged(object? sender, ConnectionStatus status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ConnectionStatusText = status switch
            {
                ConnectionStatus.Connected => Resources.Strings.Connected,
                ConnectionStatus.Connecting => Resources.Strings.Connecting,
                ConnectionStatus.Reconnecting => Resources.Strings.Reconnecting,
                ConnectionStatus.Disconnected => Resources.Strings.Disconnected,
                ConnectionStatus.Error => "Error",
                _ => status.ToString()
            };

            ConnectionStatusColor = status switch
            {
                ConnectionStatus.Connected => "#FF00D68F",
                ConnectionStatus.Connecting or ConnectionStatus.Reconnecting => "#FFFFAA00",
                _ => "#FFFF3D71"
            };
        });
    }
}
