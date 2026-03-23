using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using ShippingAPR.App.Views;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IVesselStore _vesselStore;
    private readonly IVesselTrackingService _trackingService;
    private readonly IConfiguration _configuration;

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

    [ObservableProperty]
    private bool _hasApiKey;

    public string StartTrackingTooltip => HasApiKey
        ? "Connect to AIS stream and start tracking vessels"
        : "Add an API key first to enable tracking";

    public MapViewModel MapViewModel { get; }
    public VesselListViewModel VesselListViewModel { get; }
    public VesselDetailViewModel VesselDetailViewModel { get; }
    public FilterViewModel FilterViewModel { get; }
    public SearchViewModel SearchViewModel { get; }

    public MainViewModel(
        IVesselStore vesselStore,
        IVesselTrackingService trackingService,
        IConfiguration configuration,
        MapViewModel mapViewModel,
        VesselListViewModel vesselListViewModel,
        VesselDetailViewModel vesselDetailViewModel,
        FilterViewModel filterViewModel,
        SearchViewModel searchViewModel)
    {
        _vesselStore = vesselStore;
        _trackingService = trackingService;
        _configuration = configuration;

        // Check if API key is configured
        HasApiKey = !string.IsNullOrEmpty(configuration["AisStream:ApiKey"]);

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
        var themeFile = IsDarkTheme ? "DarkTheme.xaml" : "LightTheme.xaml";
        var themeUri = new Uri($"pack://application:,,,/Assets/Themes/{themeFile}");

        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = themeUri });
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

    [RelayCommand]
    private void OpenApiKeyDialog()
    {
        var dialog = new WelcomeDialog();
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.ApiKey))
        {
            SaveApiKey(dialog.ApiKey);
            HasApiKey = true;
            OnPropertyChanged(nameof(StartTrackingTooltip));
        }
    }

    partial void OnHasApiKeyChanged(bool value)
    {
        OnPropertyChanged(nameof(StartTrackingTooltip));
    }

    internal static void SaveApiKey(string apiKey)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            var baseDir = exePath is not null
                ? Path.GetDirectoryName(exePath)
                : AppContext.BaseDirectory;
            var path = Path.Combine(baseDir ?? AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            json = json.Replace("\"ApiKey\": \"\"", $"\"ApiKey\": \"{apiKey}\"");
            File.WriteAllText(path, json);
        }
        catch
        {
            // Don't crash if we can't save the API key
        }
    }

    private void OnConnectionStatusChanged(object? sender, ConnectionStatus status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ConnectionStatusText = status switch
            {
                ConnectionStatus.Connected => "Connected",
                ConnectionStatus.Connecting => "Connecting...",
                ConnectionStatus.Reconnecting => "Reconnecting...",
                ConnectionStatus.Disconnected => "Disconnected",
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
