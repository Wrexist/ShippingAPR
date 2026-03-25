using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShippingAPR.App.Configuration;
using ShippingAPR.App.ViewModels;
using ShippingAPR.App.Views;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Infrastructure.AisStream;
using ShippingAPR.Infrastructure.Mapping;
using ShippingAPR.Infrastructure.Ports;
using ShippingAPR.Infrastructure.VesselFinder;
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
            ShowStartupError(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ShowStartupError(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ShowStartupError(args.Exception);
            args.SetObserved();
        };

        try
        {
            await StartupAsync();
        }
        catch (Exception ex)
        {
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

                // Core infrastructure
                services.AddSingleton<AisMessageMapper>();
                services.AddSingleton<PortRepository>();
                services.AddSingleton<IPortRepository>(sp => sp.GetRequiredService<PortRepository>());
                services.AddSingleton<IAisStreamClient, AisStreamClient>();
                services.AddHttpClient<IVesselEnrichmentClient, VesselFinderClient>();

                // Services
                services.AddSingleton<VesselStore>();
                services.AddSingleton<IVesselStore>(sp => sp.GetRequiredService<VesselStore>());
                services.AddSingleton<VesselTrackingService>();
                services.AddSingleton<IVesselTrackingService>(sp => sp.GetRequiredService<VesselTrackingService>());
                services.AddHostedService(sp => sp.GetRequiredService<VesselTrackingService>());
                services.AddSingleton<AreaMonitorService>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<WatchlistService>();
                services.AddSingleton<IWatchlistService>(sp => sp.GetRequiredService<WatchlistService>());
                services.AddSingleton<ExportService>();
                services.AddSingleton<StatisticsService>();
                services.AddHostedService<CollisionRiskService>();

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

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Validate configuration at startup
        ValidateConfiguration(_host.Services);

        // Load persisted user preferences
        var prefs = _host.Services.GetRequiredService<UserPreferences>();
        prefs.Load();

        // Set sync context for UI thread dispatching
        var vesselStore = _host.Services.GetRequiredService<VesselStore>();
        vesselStore.SetSynchronizationContext(SynchronizationContext.Current);

        // Initialize services that need eager construction
        _host.Services.GetRequiredService<AreaMonitorService>();
        _host.Services.GetRequiredService<NotificationService>();
        _host.Services.GetRequiredService<WatchlistService>();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();

        // Check if API key is configured
        var config = _host.Services.GetRequiredService<IConfiguration>();
        var apiKey = config["AisStream:ApiKey"];

        if (string.IsNullOrEmpty(apiKey))
        {
            var welcomeDialog = new WelcomeDialog();
            if (welcomeDialog.ShowDialog() == true && !string.IsNullOrEmpty(welcomeDialog.ApiKey))
            {
                // Save API key to appsettings.json
                MainViewModel.SaveApiKey(welcomeDialog.ApiKey);
                mainViewModel.HasApiKey = true;
            }
        }

        mainWindow.Show();
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

        // Warn about missing optional keys
        var vesselFinderKey = config[$"{VesselFinderOptions.SectionName}:ApiKey"];
        if (string.IsNullOrEmpty(vesselFinderKey))
            logger.LogInformation("VesselFinder API key not configured — vessel enrichment will be disabled");
    }

    private static void ShowStartupError(Exception ex)
    {
        var message = $"ShippingAPR failed to start.\n\n{ex.GetType().Name}: {ex.Message}";
        if (ex.InnerException is not null)
            message += $"\n\nInner: {ex.InnerException.Message}";
        message += $"\n\nStack trace:\n{ex.StackTrace}";

        MessageBox.Show(message, "ShippingAPR - Startup Error",
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
