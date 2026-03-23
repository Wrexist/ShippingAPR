using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
                var exePath = Environment.ProcessPath
                    ?? Assembly.GetExecutingAssembly().Location;
                var baseDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

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

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MapViewModel>();
                services.AddSingleton<VesselDetailViewModel>();
                services.AddSingleton<VesselListViewModel>();
                services.AddSingleton<FilterViewModel>();
                services.AddSingleton<SearchViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Set sync context for UI thread dispatching
        var vesselStore = _host.Services.GetRequiredService<VesselStore>();
        vesselStore.SetSynchronizationContext(SynchronizationContext.Current);

        // Initialize services that need eager construction
        _host.Services.GetRequiredService<AreaMonitorService>();
        _host.Services.GetRequiredService<NotificationService>();

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
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        base.OnExit(e);
    }

}
