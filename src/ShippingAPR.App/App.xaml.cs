using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShippingAPR.App.Configuration;
using ShippingAPR.App.ViewModels;
using ShippingAPR.App.Views;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Enums;
using ShippingAPR.Infrastructure;
using ShippingAPR.Infrastructure.AisStream;
using ShippingAPR.Infrastructure.Datalastic;
using ShippingAPR.Infrastructure.DataDocked;
using ShippingAPR.Infrastructure.Http;
using ShippingAPR.Infrastructure.Mapping;
using ShippingAPR.Infrastructure.Persistence;
using ShippingAPR.Infrastructure.Ports;
using ShippingAPR.Infrastructure.VesselFinder;
using ShippingAPR.Infrastructure.Weather;
using ShippingAPR.Services;

namespace ShippingAPR.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch any unhandled exceptions so the app doesn't silently crash
        DispatcherUnhandledException += (_, args) =>
        {
            LogToCrashFile(args.Exception, nameof(DispatcherUnhandledException));
            ShowStartupError(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogToCrashFile(ex, nameof(AppDomain.UnhandledException));
                ShowStartupError(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogToCrashFile(args.Exception, nameof(TaskScheduler.UnobservedTaskException));
            ShowStartupError(args.Exception);
            args.SetObserved();
        };

        try
        {
            await StartupAsync();
        }
        catch (Exception ex)
        {
            LogToCrashFile(ex, "Startup");
            ShowStartupError(ex);
            Shutdown(1);
        }
    }

    private async Task StartupAsync()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                // For single-file published apps, AppContext.BaseDirectory may point
                // to a temp extraction dir. Use the exe's actual directory instead.
                var exePath = Environment.ProcessPath;
                var baseDir = exePath is not null
                    ? Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
                    : AppContext.BaseDirectory;

                config.SetBasePath(baseDir);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddJsonFile("appsettings.Development.json", optional: true);

                // Per-user overrides (including API keys) live under %APPDATA%, not the
                // install directory. Added last so it takes precedence over shipped defaults.
                var localDir = Path.GetDirectoryName(LocalSettingsStore.FilePath);
                if (localDir is not null)
                {
                    Directory.CreateDirectory(localDir);
                    config.AddJsonFile(new PhysicalFileProvider(localDir),
                        Path.GetFileName(LocalSettingsStore.FilePath),
                        optional: true, reloadOnChange: true);
                }
            })
            .ConfigureServices((ctx, services) =>
            {
                // Options
                services.Configure<AisStreamOptions>(
                    ctx.Configuration.GetSection(AisStreamOptions.SectionName));
                services.Configure<VesselFinderOptions>(
                    ctx.Configuration.GetSection(VesselFinderOptions.SectionName));
                services.Configure<TrackingOptions>(
                    ctx.Configuration.GetSection(TrackingOptions.SectionName));
                services.Configure<UiOptions>(
                    ctx.Configuration.GetSection(UiOptions.SectionName));
                services.Configure<MarineWeatherOptions>(
                    ctx.Configuration.GetSection(MarineWeatherOptions.SectionName));
                services.Configure<TrackHistoryOptions>(
                    ctx.Configuration.GetSection(TrackHistoryOptions.SectionName));

                // Provider options
                services.Configure<DatalasticOptions>(
                    ctx.Configuration.GetSection(DatalasticOptions.SectionName));
                services.Configure<DataDockedOptions>(
                    ctx.Configuration.GetSection(DataDockedOptions.SectionName));

                // Core infrastructure
                services.AddSingleton<AisMessageMapper>();
                services.AddSingleton<PortRepository>();
                services.AddSingleton<IPortRepository>(sp => sp.GetRequiredService<PortRepository>());

                // AIS data providers (concrete types for factory resolution)
                services.AddSingleton<AisStreamClient>();
                services.AddHttpClient<DatalasticClient>()
                    .AddHttpMessageHandler(() => new ResilientHttpHandler());
                services.AddHttpClient<DataDockedClient>()
                    .AddHttpMessageHandler(() => new ResilientHttpHandler());
                services.AddSingleton<AisProviderFactory>();

                // Wire up the active provider with optional fallback
                services.AddSingleton<IAisDataProvider>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    var factory = sp.GetRequiredService<AisProviderFactory>();
                    var logger = sp.GetRequiredService<ILogger<FallbackAisProvider>>();

                    var activeType = Enum.TryParse<AisProviderType>(config["AisProvider:Active"], true, out var a)
                        ? a : AisProviderType.AisStream;
                    var primary = factory.Create(activeType);

                    IAisDataProvider? fallback = null;
                    if (Enum.TryParse<AisProviderType>(config["AisProvider:Fallback"], true, out var f))
                        fallback = factory.Create(f);

                    return new FallbackAisProvider(primary, fallback, logger);
                });

                services.AddHttpClient<IVesselEnrichmentClient, VesselFinderClient>()
                    .AddHttpMessageHandler(() => new ResilientHttpHandler());

                // Weather & ocean data clients
                services.AddHttpClient<IMarineWeatherClient, OpenMeteoMarineClient>()
                    .AddHttpMessageHandler(() => new ResilientHttpHandler());
                services.AddHttpClient<ITideDataClient, TideDataClient>()
                    .AddHttpMessageHandler(() => new ResilientHttpHandler());

                // Services
                services.AddSingleton<VesselStore>();
                services.AddSingleton<IVesselStore>(sp => sp.GetRequiredService<VesselStore>());

                // Durable track-history persistence (SQLite)
                services.AddSingleton<ITrackHistoryStore, SqliteTrackHistoryStore>();
                services.AddSingleton<TrackPersistenceService>();
                services.AddHostedService(sp => sp.GetRequiredService<TrackPersistenceService>());
                services.AddSingleton<VesselTrackingService>();
                services.AddSingleton<IVesselTrackingService>(sp => sp.GetRequiredService<VesselTrackingService>());
                services.AddHostedService(sp => sp.GetRequiredService<VesselTrackingService>());
                services.AddSingleton<AreaMonitorService>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<AnomalyDetectionService>();
                services.AddSingleton<WatchlistService>();
                services.AddSingleton<IWatchlistService>(sp => sp.GetRequiredService<WatchlistService>());
                services.AddSingleton<ExportService>();
                services.AddSingleton<StatisticsService>();
                services.AddHostedService<CollisionRiskService>();
                services.AddSingleton<AchievementService>();
                services.AddSingleton<VoyageNarrativeService>();
                services.AddSingleton<HeatmapService>();
                services.AddSingleton<PortActivityService>();
                services.AddSingleton<AlertEngine>();
                services.AddSingleton<WeatherOverlayService>();
                services.AddSingleton<WeatherAlertService>();
                services.AddHostedService(sp => sp.GetRequiredService<WeatherAlertService>());
                services.AddSingleton<FleetService>();
                services.AddSingleton<SpotlightService>();
                services.AddSingleton<EmissionsEstimatorService>();
                services.AddSingleton<ChokepointMonitorService>();
                services.AddSingleton<EncounterJournalService>();
                services.AddSingleton<ShipmentTrackingService>();
                services.AddSingleton<MaritimeIncidentService>();

                // User preferences (persisted between sessions)
                services.AddSingleton<UserPreferences>();

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MapViewModel>();
                services.AddSingleton<VesselDetailViewModel>();
                services.AddSingleton<VesselListViewModel>();
                services.AddSingleton<FilterViewModel>();
                services.AddSingleton<SearchViewModel>();
                services.AddSingleton<StatisticsViewModel>();
                services.AddSingleton<NotificationCenterViewModel>();
                services.AddSingleton<AchievementViewModel>();
                services.AddSingleton<PortDashboardViewModel>();
                services.AddSingleton<AlertRuleViewModel>();
                services.AddSingleton<ChokepointViewModel>();
                services.AddSingleton<EncounterJournalViewModel>();
                services.AddSingleton<ShipmentViewModel>();
                services.AddSingleton<MaritimeNewsViewModel>();
                services.AddSingleton<VesselComparisonViewModel>();
                services.AddSingleton<GeofenceViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Validate configuration at startup
        ValidateConfiguration(_host.Services);

        // Load persisted user preferences and apply theme + language BEFORE any
        // window/ViewModel is created so they actually survive a restart.
        var prefs = _host.Services.GetRequiredService<UserPreferences>();
        prefs.Load();
        ApplyStartupPreferences(prefs);

        // Set sync context for UI thread dispatching
        var vesselStore = _host.Services.GetRequiredService<VesselStore>();
        vesselStore.SetSynchronizationContext(SynchronizationContext.Current);

        // Initialize services that need eager construction
        _host.Services.GetRequiredService<AreaMonitorService>();
        _host.Services.GetRequiredService<NotificationService>();
        _host.Services.GetRequiredService<AnomalyDetectionService>();
        _host.Services.GetRequiredService<WatchlistService>();
        _host.Services.GetRequiredService<AchievementService>();
        _host.Services.GetRequiredService<VoyageNarrativeService>();
        _host.Services.GetRequiredService<HeatmapService>();
        _host.Services.GetRequiredService<PortActivityService>();
        _host.Services.GetRequiredService<AlertEngine>();
        _host.Services.GetRequiredService<WeatherOverlayService>();
        _host.Services.GetRequiredService<WeatherAlertService>();
        _host.Services.GetRequiredService<FleetService>();
        _host.Services.GetRequiredService<SpotlightService>();
        _host.Services.GetRequiredService<ChokepointMonitorService>();
        _host.Services.GetRequiredService<EncounterJournalService>();
        _host.Services.GetRequiredService<ShipmentTrackingService>();
        _host.Services.GetRequiredService<MaritimeIncidentService>();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();

        // Reflect persisted theme/language and filters in the ViewModels.
        mainViewModel.IsDarkTheme = prefs.IsDarkTheme;
        mainViewModel.CurrentLanguage = prefs.Language;
        ApplyFilterPreferences(prefs, mainViewModel.FilterViewModel);

        // Check whether the ACTIVE provider has a key configured (not just AisStream).
        var config = _host.Services.GetRequiredService<IConfiguration>();
        var activeProvider = config["AisProvider:Active"] ?? "AisStream";
        var apiKey = activeProvider switch
        {
            "Datalastic" => config["Datalastic:ApiKey"],
            "DataDocked" => config["DataDocked:ApiKey"],
            _ => config["AisStream:ApiKey"]
        };

        if (string.IsNullOrEmpty(apiKey))
        {
            var welcomeDialog = new WelcomeDialog();
            if (welcomeDialog.ShowDialog() == true && !string.IsNullOrEmpty(welcomeDialog.ApiKey))
            {
                // Save the key to the section for the provider the user actually chose.
                if (!MainViewModel.SaveProviderApiKey(welcomeDialog.SelectedProvider, welcomeDialog.ApiKey))
                {
                    MessageBox.Show(
                        "Failed to save the API key.\nYou can add it manually in Settings.",
                        "ShippingAPR", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                mainViewModel.HasApiKey = true;
            }
        }

        mainWindow.Show();
    }

    private void ApplyStartupPreferences(UserPreferences prefs)
    {
        // Theme: App.xaml ships the dark theme; swap to light if persisted.
        if (!prefs.IsDarkTheme)
        {
            try
            {
                var themeUri = new Uri("pack://application:,,,/Assets/Themes/LightTheme.xaml");
                Resources.MergedDictionaries.Clear();
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeUri });
            }
            catch (Exception ex)
            {
                LogToCrashFile(ex, "ApplyStartupTheme");
            }
        }

        // Language: set the UI culture so localized resources resolve correctly.
        var culture = new System.Globalization.CultureInfo(prefs.Language == "sv" ? "sv-SE" : "en-US");
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static void ApplyFilterPreferences(UserPreferences prefs, ViewModels.FilterViewModel filter)
    {
        filter.ShowCargo = prefs.ShowCargo;
        filter.ShowTanker = prefs.ShowTanker;
        filter.ShowPassenger = prefs.ShowPassenger;
        filter.ShowFishing = prefs.ShowFishing;
        filter.ShowTugPilot = prefs.ShowTugPilot;
        filter.ShowOther = prefs.ShowOther;
        filter.MaxSpeed = prefs.MaxSpeed;
        filter.DestinationFilter = prefs.DestinationFilter;
        filter.FlagFilter = prefs.FlagFilter;
    }

    private static void ValidateConfiguration(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<App>>();
        var config = services.GetRequiredService<IConfiguration>();

        // Validate AisStream settings
        var aisSection = config.GetSection(AisStreamOptions.SectionName);
        var wsUrl = aisSection["WebSocketUrl"];
        if (!string.IsNullOrEmpty(wsUrl) && !Uri.TryCreate(wsUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"AisStream:WebSocketUrl is not a valid URI: '{wsUrl}'");

        var connectTimeout = aisSection.GetValue<int>("ConnectTimeoutSeconds");
        if (connectTimeout < 0)
            throw new InvalidOperationException("AisStream:ConnectTimeoutSeconds must be non-negative");

        // Validate Tracking settings
        var trackingSection = config.GetSection(TrackingOptions.SectionName);
        var portCacheMaxSize = trackingSection.GetValue<int>("PortCacheMaxSize");
        if (portCacheMaxSize < 0)
            throw new InvalidOperationException("Tracking:PortCacheMaxSize must be non-negative");

        // Validate UI settings
        var uiSection = config.GetSection(UiOptions.SectionName);
        var mapBatchMs = uiSection.GetValue<int>("MapBatchUpdateMs");
        if (mapBatchMs < 0)
            throw new InvalidOperationException("Ui:MapBatchUpdateMs must be non-negative");
        var vesselListMs = uiSection.GetValue<int>("VesselListRefreshMs");
        if (vesselListMs < 0)
            throw new InvalidOperationException("Ui:VesselListRefreshMs must be non-negative");

        // Validate AisStream reconnection settings
        var maxReconnectAttempts = aisSection.GetValue<int>("ReconnectMaxAttempts");
        if (maxReconnectAttempts < 0)
            throw new InvalidOperationException("AisStream:ReconnectMaxAttempts must be non-negative");

        // Warn about missing optional keys
        var vesselFinderKey = config[$"{VesselFinderOptions.SectionName}:ApiKey"];
        if (string.IsNullOrEmpty(vesselFinderKey))
            logger.LogInformation("VesselFinder API key not configured — vessel enrichment will be disabled");
    }

    /// <summary>Directory where crash logs are written (%LOCALAPPDATA%/ShippingAPR/logs).</summary>
    private static string CrashLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR", "logs");

    /// <summary>
    /// Appends an unhandled exception to a daily crash log so a field crash leaves
    /// a diagnostic trail rather than only a transient dialog. Never throws — a
    /// failure here must not mask the original exception.
    /// </summary>
    private static void LogToCrashFile(Exception ex, string source)
    {
        try
        {
            Directory.CreateDirectory(CrashLogDirectory);
            var file = Path.Combine(CrashLogDirectory, $"crash-{DateTime.Now:yyyyMMdd}.log");
            var entry =
                $"[{DateTime.Now:O}] ({source}) {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
            File.AppendAllText(file, entry);
        }
        catch
        {
            // Crash logging is best-effort; swallow any I/O failure.
        }
    }

    private static void ShowStartupError(Exception ex)
    {
        var message = $"ShippingAPR hit an unexpected error.\n\n{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException is not null)
            message += $"\n\nCause: {ex.InnerException.Message}";

        // Add actionable guidance based on error type
        message += ex switch
        {
            InvalidOperationException => "\n\nTip: Check your appsettings.json for invalid configuration values.",
            System.IO.FileNotFoundException => "\n\nTip: Ensure appsettings.json is in the same directory as the executable.",
            _ => "\n\nTip: Try deleting user preferences at %APPDATA%/ShippingAPR and restarting."
        };

        message += $"\n\nA detailed log was saved to:\n{CrashLogDirectory}";

#if DEBUG
        message += $"\n\nStack trace:\n{ex.StackTrace}";
#endif

        MessageBox.Show(message, "ShippingAPR - Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Dispose ViewModels to stop timers and unsubscribe events
            _host.Services.GetRequiredService<MainViewModel>().Dispose();

            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        base.OnExit(e);
    }

}
