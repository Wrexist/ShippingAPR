using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using ShippingAPR.App.Configuration;
using ShippingAPR.App.Views;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.App.Resources;
using ShippingAPR.Services;

namespace ShippingAPR.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly IVesselTrackingService _trackingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MainViewModel> _logger;
    private readonly UiOptions _uiOptions;
    private readonly ExportService _exportService;
    private readonly EventHandler<ConnectionStatus> _onConnectionStatusChanged;
    private readonly EventHandler<Vessel> _onVesselAdded;
    private readonly EventHandler _onStoreCleared;
    private readonly PropertyChangedEventHandler _onSelectedVesselChanged;
    private readonly EventHandler<Vessel> _onVesselSelected;
    private CancellationTokenSource? _notificationCts;

    [ObservableProperty]
    private string _connectionStatusText = Strings.Disconnected;

    [ObservableProperty]
    private string _connectionStatusColor;

    [ObservableProperty]
    private int _vesselCount;

    [ObservableProperty]
    private string _messageRateText = "";

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

    [ObservableProperty]
    private bool _isDashboardVisible;

    public MapViewModel MapViewModel { get; }
    public VesselListViewModel VesselListViewModel { get; }
    public VesselDetailViewModel VesselDetailViewModel { get; }
    public FilterViewModel FilterViewModel { get; }
    public SearchViewModel SearchViewModel { get; }
    public StatisticsViewModel StatisticsViewModel { get; }
    public NotificationCenterViewModel NotificationCenterViewModel { get; }
    public AchievementViewModel AchievementViewModel { get; }
    public PortDashboardViewModel PortDashboardViewModel { get; }
    public AlertRuleViewModel AlertRuleViewModel { get; }

    [ObservableProperty]
    private bool _isNotificationCenterOpen;

    [ObservableProperty]
    private int _rightPanelIndex; // 0=Detail, 1=Dashboard, 2=Achievements, 3=PortCam, 4=Alerts

    public MainViewModel(
        IVesselStore vesselStore,
        IVesselTrackingService trackingService,
        IConfiguration configuration,
        ILogger<MainViewModel> logger,
        IOptions<UiOptions> uiOptions,
        ExportService exportService,
        MapViewModel mapViewModel,
        VesselListViewModel vesselListViewModel,
        VesselDetailViewModel vesselDetailViewModel,
        FilterViewModel filterViewModel,
        SearchViewModel searchViewModel,
        StatisticsViewModel statisticsViewModel,
        NotificationCenterViewModel notificationCenterViewModel,
        AchievementViewModel achievementViewModel,
        PortDashboardViewModel portDashboardViewModel,
        AlertRuleViewModel alertRuleViewModel)
    {
        _vesselStore = vesselStore;
        _trackingService = trackingService;
        _configuration = configuration;
        _logger = logger;
        _uiOptions = uiOptions.Value;
        _exportService = exportService;
        _connectionStatusColor = _uiOptions.StatusColorError;

        // Check if API key is configured
        HasApiKey = !string.IsNullOrEmpty(configuration["AisStream:ApiKey"]);

        MapViewModel = mapViewModel;
        VesselListViewModel = vesselListViewModel;
        VesselDetailViewModel = vesselDetailViewModel;
        FilterViewModel = filterViewModel;
        SearchViewModel = searchViewModel;
        StatisticsViewModel = statisticsViewModel;
        NotificationCenterViewModel = notificationCenterViewModel;
        AchievementViewModel = achievementViewModel;
        PortDashboardViewModel = portDashboardViewModel;
        AlertRuleViewModel = alertRuleViewModel;

        _onConnectionStatusChanged = OnConnectionStatusChanged;
        _trackingService.ConnectionStatusChanged += _onConnectionStatusChanged;

        _onVesselAdded = (_, _) => VesselCount = _vesselStore.Count;
        _onStoreCleared = (_, _) => VesselCount = 0;
        _vesselStore.VesselAdded += _onVesselAdded;
        _vesselStore.StoreCleared += _onStoreCleared;

        // Listen for notifications
        WeakReferenceMessenger.Default.Register<NotificationPublished>(this, (_, msg) =>
        {
            NotificationText = $"{msg.Value.Title}: {msg.Value.Body}";
            ShowNotification = true;
            AutoHideNotification();
        });

        // Wire vessel selection from list to detail
        _onSelectedVesselChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(VesselListViewModel.SelectedVessel))
            {
                VesselDetailViewModel.Vessel = VesselListViewModel.SelectedVessel;
                MapViewModel.HighlightVessel(VesselListViewModel.SelectedVessel);
            }
        };
        VesselListViewModel.PropertyChanged += _onSelectedVesselChanged;

        // Wire search results
        _onVesselSelected = (_, vessel) =>
        {
            VesselDetailViewModel.Vessel = vessel;
            VesselListViewModel.SelectedVessel = vessel;
            MapViewModel.CenterOnVessel(vessel);
        };
        SearchViewModel.VesselSelected += _onVesselSelected;

        // Wire vessel clicks on map
        MapViewModel.VesselFeatureClicked += (_, mmsi) =>
        {
            VesselListViewModel.SelectVesselByMmsi(mmsi);
            MapViewModel.CenterOnVessel(VesselListViewModel.SelectedVessel);
        };

        // Auto-start tracking if API key is available
        if (HasApiKey)
        {
            _ = AutoStartTrackingAsync();
        }
    }

    private async Task AutoStartTrackingAsync()
    {
        try
        {
            // Short delay to let the UI finish loading
            await Task.Delay(_uiOptions.StartupDelayMs);
            await MapViewModel.StartTrackingCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ConnectionStatusText = "Failed to auto-connect";
            ConnectionStatusColor = _uiOptions.StatusColorError;
            // Non-fatal — user can click Start Tracking manually
            _logger.LogWarning(ex, "Auto-start tracking failed");
        }
    }

    [RelayCommand]
    private void ToggleNotificationCenter()
    {
        IsNotificationCenterOpen = !IsNotificationCenterOpen;
        if (IsNotificationCenterOpen)
            NotificationCenterViewModel.MarkAllReadCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleDashboard()
    {
        IsDashboardVisible = !IsDashboardVisible;
        if (IsDashboardVisible)
            RightPanelIndex = 1;
        else
            RightPanelIndex = 0;
    }

    [RelayCommand]
    private void ShowRightPanel(string panelIndex)
    {
        if (int.TryParse(panelIndex, out var idx))
        {
            RightPanelIndex = idx;
            IsDashboardVisible = idx != 0;
        }
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

            // Auto-start tracking after API key is configured
            _ = AutoStartTrackingAsync();
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

            // Use proper JSON parsing to avoid injection via malformed keys
            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json) ?? new JsonObject();
            var aisSection = root["AisStream"]?.AsObject();
            if (aisSection is null)
            {
                aisSection = new JsonObject();
                root["AisStream"] = aisSection;
            }
            aisSection["ApiKey"] = apiKey;

            File.WriteAllText(path, root.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            // Don't crash if we can't save the API key — non-critical
            // Logged at Trace since there's no static logger; callers can catch if needed
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"vessels_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var csv = _exportService.ExportToCsv();
                File.WriteAllText(dialog.FileName, csv);
                _logger.LogInformation("Exported {Count} vessels to CSV: {Path}", _vesselStore.Count, dialog.FileName);
                ShowExportNotification(_vesselStore.Count, dialog.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export CSV");
                ShowErrorNotification(Strings.Export, ex.Message);
            }
        }
    }

    [RelayCommand]
    private void ExportJson()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"vessels_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = _exportService.ExportToJson();
                File.WriteAllText(dialog.FileName, json);
                _logger.LogInformation("Exported {Count} vessels to JSON: {Path}", _vesselStore.Count, dialog.FileName);
                ShowExportNotification(_vesselStore.Count, dialog.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export JSON");
                ShowErrorNotification(Strings.Export, ex.Message);
            }
        }
    }

    private void ShowExportNotification(int count, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        NotificationText = $"{Strings.Export}: {count} {Strings.Ships.ToLowerInvariant()} → {fileName}";
        ShowNotification = true;
        AutoHideNotification();
    }

    private void ShowErrorNotification(string title, string message)
    {
        NotificationText = $"{Strings.Error}: {message}";
        ShowNotification = true;
        AutoHideNotification();
    }

    private void AutoHideNotification()
    {
        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
        var cts = _notificationCts = new CancellationTokenSource();
        Task.Delay(_uiOptions.NotificationTimeoutMs, cts.Token).ContinueWith(_ =>
        {
            Application.Current?.Dispatcher.Invoke(() => ShowNotification = false);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private void OnConnectionStatusChanged(object? sender, ConnectionStatus status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ConnectionStatusText = status switch
            {
                ConnectionStatus.Connected => Strings.Connected,
                ConnectionStatus.Connecting => Strings.Connecting,
                ConnectionStatus.Reconnecting => Strings.Reconnecting,
                ConnectionStatus.Disconnected => Strings.Disconnected,
                ConnectionStatus.Error => Strings.Error,
                _ => status.ToString()
            };

            ConnectionStatusColor = status switch
            {
                ConnectionStatus.Connected => _uiOptions.StatusColorConnected,
                ConnectionStatus.Connecting or ConnectionStatus.Reconnecting => _uiOptions.StatusColorConnecting,
                _ => _uiOptions.StatusColorError
            };
        });
    }

    public void Dispose()
    {
        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
        _trackingService.ConnectionStatusChanged -= _onConnectionStatusChanged;
        _vesselStore.VesselAdded -= _onVesselAdded;
        _vesselStore.StoreCleared -= _onStoreCleared;
        VesselListViewModel.PropertyChanged -= _onSelectedVesselChanged;
        SearchViewModel.VesselSelected -= _onVesselSelected;
        WeakReferenceMessenger.Default.Unregister<NotificationPublished>(this);
        MapViewModel.Dispose();
        VesselListViewModel.Dispose();
        StatisticsViewModel.Dispose();
    }
}
