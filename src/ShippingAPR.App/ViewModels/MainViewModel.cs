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
        ? Strings.ResourceManager.GetString("StartTrackingTooltipReady", null) ?? "Connect to AIS stream and start tracking vessels"
        : Strings.ResourceManager.GetString("StartTrackingTooltipNoKey", null) ?? "Add an API key first to enable tracking";

    [ObservableProperty]
    private bool _isDashboardVisible;

    public MapViewModel MapViewModel { get; }
    public VesselListViewModel VesselListViewModel { get; }
    public VesselDetailViewModel VesselDetailViewModel { get; }
    public FilterViewModel FilterViewModel { get; }
    public SearchViewModel SearchViewModel { get; }
    public StatisticsViewModel StatisticsViewModel { get; }
    public NotificationCenterViewModel NotificationCenterViewModel { get; }
    public GeofenceViewModel GeofenceViewModel { get; }
    public AchievementViewModel AchievementViewModel { get; }
    public PortDashboardViewModel PortDashboardViewModel { get; }
    public AlertRuleViewModel AlertRuleViewModel { get; }
    public ChokepointViewModel ChokepointViewModel { get; }
    public EncounterJournalViewModel EncounterJournalViewModel { get; }
    public ShipmentViewModel ShipmentViewModel { get; }
    public MaritimeNewsViewModel MaritimeNewsViewModel { get; }
    public VesselComparisonViewModel VesselComparisonViewModel { get; }

    [ObservableProperty]
    private bool _isNotificationCenterOpen;

    [ObservableProperty]
    private int _rightPanelIndex; // 0=Detail, 1=Dashboard, 2=Achievements, 3=PortCam, 4=Alerts, 5=Chokepoints, 6=Journal, 7=Shipments, 8=News

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
        AlertRuleViewModel alertRuleViewModel,
        ChokepointViewModel chokepointViewModel,
        EncounterJournalViewModel encounterJournalViewModel,
        ShipmentViewModel shipmentViewModel,
        MaritimeNewsViewModel maritimeNewsViewModel,
        VesselComparisonViewModel vesselComparisonViewModel,
        GeofenceViewModel geofenceViewModel)
    {
        _vesselStore = vesselStore;
        _trackingService = trackingService;
        _configuration = configuration;
        _logger = logger;
        _uiOptions = uiOptions.Value;
        _exportService = exportService;
        _connectionStatusColor = _uiOptions.StatusColorError;

        // Check if API key is configured for the active provider
        var activeProvider = configuration["AisProvider:Active"] ?? "AisStream";
        HasApiKey = activeProvider switch
        {
            "Datalastic" => !string.IsNullOrEmpty(configuration["Datalastic:ApiKey"]),
            "DataDocked" => !string.IsNullOrEmpty(configuration["DataDocked:ApiKey"]),
            _ => !string.IsNullOrEmpty(configuration["AisStream:ApiKey"])
        };

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
        ChokepointViewModel = chokepointViewModel;
        EncounterJournalViewModel = encounterJournalViewModel;
        ShipmentViewModel = shipmentViewModel;
        MaritimeNewsViewModel = maritimeNewsViewModel;
        VesselComparisonViewModel = vesselComparisonViewModel;
        GeofenceViewModel = geofenceViewModel;

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
            AutoStartTrackingAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Unhandled error in auto-start tracking");
            }, TaskContinuationOptions.OnlyOnFaulted);
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
            ConnectionStatusText = Strings.Error;
            ConnectionStatusColor = _uiOptions.StatusColorError;
            ShowToast($"{Strings.Error}: Auto-connect failed — click Start Tracking to retry");
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
    private void OpenSettings()
    {
        var settingsVm = new SettingsViewModel(_configuration);
        var dialog = new SettingsDialog(settingsVm);
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        var themeFile = IsDarkTheme ? "DarkTheme.xaml" : "LightTheme.xaml";
        var themeUri = new Uri($"pack://application:,,,/Assets/Themes/{themeFile}");

        try
        {
            var newTheme = new ResourceDictionary { Source = themeUri };
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(newTheme);

            // Sync map tiles with theme
            MapViewModel.SwitchMapLayerCommand.Execute(IsDarkTheme ? "Dark" : "Standard");
        }
        catch (Exception ex)
        {
            // Revert toggle on failure so UI state stays consistent
            IsDarkTheme = !IsDarkTheme;
            _logger.LogError(ex, "Failed to load theme {Theme}", themeFile);
        }
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
    private void ShowKeyboardShortcuts()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "1.0.0";

        var shortcuts = new System.Text.StringBuilder();
        shortcuts.AppendLine($"ShippingAPR v{version}");
        shortcuts.AppendLine();
        shortcuts.AppendLine("Keyboard Shortcuts:");
        shortcuts.AppendLine();
        shortcuts.AppendLine("Ctrl+F\t\tSearch vessels");
        shortcuts.AppendLine("Ctrl+E\t\tExport to CSV");
        shortcuts.AppendLine("Ctrl+N\t\tToggle notifications");
        shortcuts.AppendLine("Ctrl+M\t\tToggle measurement tool");
        shortcuts.AppendLine("Ctrl+W\t\tToggle weather overlay");
        shortcuts.AppendLine("+/-\t\tZoom in/out");
        shortcuts.AppendLine("1-9\t\tSwitch right panel tabs");
        shortcuts.AppendLine("F11\t\tToggle fullscreen");
        shortcuts.AppendLine("F1\t\tThis help dialog");
        shortcuts.AppendLine("Esc\t\tDismiss notifications");

        MessageBox.Show(shortcuts.ToString(), $"{Strings.KeyboardShortcuts} — ShippingAPR",
            MessageBoxButton.OK, MessageBoxImage.Information);
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
            AutoStartTrackingAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Unhandled error in auto-start tracking");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    partial void OnHasApiKeyChanged(bool value)
    {
        OnPropertyChanged(nameof(StartTrackingTooltip));
    }

    internal static bool SaveApiKey(string apiKey)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            var baseDir = exePath is not null
                ? Path.GetDirectoryName(exePath)
                : AppContext.BaseDirectory;
            var path = Path.Combine(baseDir ?? AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(path)) return false;

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
            return true;
        }
        catch (Exception ex)
        {
            // Non-critical — user can manually edit appsettings.json
            System.Diagnostics.Debug.WriteLine($"Failed to save API key: {ex.Message}");
            return false;
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

    [RelayCommand]
    private void ExportTrackGpx()
    {
        var vessel = VesselDetailViewModel.Vessel;
        if (vessel?.Track is null || vessel.Track.Count < 2)
        {
            ShowToast("Select a vessel with track history to export");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "GPX files (*.gpx)|*.gpx",
            DefaultExt = ".gpx",
            FileName = $"{vessel.DisplayName}_{DateTime.Now:yyyyMMdd_HHmmss}.gpx"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var gpx = _exportService.ExportTrackToGpx(vessel);
                File.WriteAllText(dialog.FileName, gpx);
                _logger.LogInformation("Exported track for {Name} to GPX: {Path}", vessel.DisplayName, dialog.FileName);
                ShowToast($"Track exported: {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export GPX");
                ShowErrorNotification("Export", ex.Message);
            }
        }
    }

    [RelayCommand]
    private void ExportTrackKml()
    {
        var vessel = VesselDetailViewModel.Vessel;
        if (vessel?.Track is null || vessel.Track.Count < 2)
        {
            ShowToast("Select a vessel with track history to export");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "KML files (*.kml)|*.kml",
            DefaultExt = ".kml",
            FileName = $"{vessel.DisplayName}_{DateTime.Now:yyyyMMdd_HHmmss}.kml"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var kml = _exportService.ExportTrackToKml(vessel);
                File.WriteAllText(dialog.FileName, kml);
                _logger.LogInformation("Exported track for {Name} to KML: {Path}", vessel.DisplayName, dialog.FileName);
                ShowToast($"Track exported: {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export KML");
                ShowErrorNotification("Export", ex.Message);
            }
        }
    }

    private void ShowExportNotification(int count, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        ShowToast($"{Strings.Export}: {count} {Strings.Ships.ToLowerInvariant()} → {fileName}");
    }

    private void ShowErrorNotification(string title, string message)
    {
        ShowToast($"{Strings.Error}: {message}");
    }

    private void ShowToast(string text)
    {
        NotificationText = text;
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
            var providerName = _configuration["AisProvider:Active"] ?? "AisStream";
            var statusBase = status switch
            {
                ConnectionStatus.Connected => Strings.Connected,
                ConnectionStatus.Connecting => Strings.Connecting,
                ConnectionStatus.Reconnecting => Strings.Reconnecting,
                ConnectionStatus.Disconnected => Strings.Disconnected,
                ConnectionStatus.Error => Strings.Error,
                ConnectionStatus.Failed => Strings.Error + " (reconnection failed)",
                _ => status.ToString()
            };

            ConnectionStatusText = status == ConnectionStatus.Connected
                ? $"{statusBase} ({providerName})"
                : statusBase;

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
        ChokepointViewModel.Dispose();
        EncounterJournalViewModel.Dispose();
        ShipmentViewModel.Dispose();
        MaritimeNewsViewModel.Dispose();
        AchievementViewModel.Dispose();
        PortDashboardViewModel.Dispose();
        AlertRuleViewModel.Dispose();
        NotificationCenterViewModel.Dispose();
    }
}
