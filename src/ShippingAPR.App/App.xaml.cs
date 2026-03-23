using System.IO;
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

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
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

        // Check if API key is configured
        var config = _host.Services.GetRequiredService<IConfiguration>();
        var apiKey = config["AisStream:ApiKey"];

        if (string.IsNullOrEmpty(apiKey))
        {
            var welcomeDialog = new WelcomeDialog();
            if (welcomeDialog.ShowDialog() == true && !string.IsNullOrEmpty(welcomeDialog.ApiKey))
            {
                // Save API key to appsettings.json
                SaveApiKey(welcomeDialog.ApiKey);

                // Update the running configuration
                var options = _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AisStreamOptions>>();
                // The reload on change will pick up the new key
            }
        }

        mainWindow.Show();
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

    private static void SaveApiKey(string apiKey)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path)) return;

        var json = File.ReadAllText(path);
        // Simple replacement for the API key field
        json = json.Replace("\"ApiKey\": \"\"", $"\"ApiKey\": \"{apiKey}\"");
        File.WriteAllText(path, json);
    }
}
