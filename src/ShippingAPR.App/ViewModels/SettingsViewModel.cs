using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using ShippingAPR.Core.Enums;

namespace ShippingAPR.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfiguration _configuration;

    [ObservableProperty] private string _aisStreamApiKey = string.Empty;
    [ObservableProperty] private string _datalasticApiKey = string.Empty;
    [ObservableProperty] private string _dataDockedApiKey = string.Empty;
    [ObservableProperty] private string _vesselFinderApiKey = string.Empty;

    [ObservableProperty] private AisProviderType _activeProvider = AisProviderType.AisStream;
    [ObservableProperty] private string _fallbackProvider = string.Empty;

    [ObservableProperty] private int _datalasticPollInterval = 15;
    [ObservableProperty] private int _dataDockedPollInterval = 15;

    public AisProviderType[] AvailableProviders { get; } = Enum.GetValues<AisProviderType>();
    public string[] FallbackOptions { get; } = ["", "AisStream", "Datalastic", "DataDocked"];

    public SettingsViewModel(IConfiguration configuration)
    {
        _configuration = configuration;
        LoadFromConfiguration();
    }

    private void LoadFromConfiguration()
    {
        AisStreamApiKey = _configuration["AisStream:ApiKey"] ?? string.Empty;
        DatalasticApiKey = _configuration["Datalastic:ApiKey"] ?? string.Empty;
        DataDockedApiKey = _configuration["DataDocked:ApiKey"] ?? string.Empty;
        VesselFinderApiKey = _configuration["VesselFinder:ApiKey"] ?? string.Empty;

        if (Enum.TryParse<AisProviderType>(_configuration["AisProvider:Active"], true, out var active))
            ActiveProvider = active;

        FallbackProvider = _configuration["AisProvider:Fallback"] ?? string.Empty;

        if (int.TryParse(_configuration["Datalastic:PollIntervalSeconds"], out var dPoll))
            DatalasticPollInterval = dPoll;
        if (int.TryParse(_configuration["DataDocked:PollIntervalSeconds"], out var ddPoll))
            DataDockedPollInterval = ddPoll;
    }

    [RelayCommand]
    private void Save()
    {
        SaveToAppsettings();
    }

    internal bool SaveToAppsettings()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            var baseDir = exePath is not null
                ? Path.GetDirectoryName(exePath)
                : AppContext.BaseDirectory;
            var path = Path.Combine(baseDir ?? AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(path)) return false;

            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json) ?? new JsonObject();

            // AisProvider section
            var providerSection = root["AisProvider"]?.AsObject() ?? new JsonObject();
            providerSection["Active"] = ActiveProvider.ToString();
            providerSection["Fallback"] = FallbackProvider;
            root["AisProvider"] = providerSection;

            // AisStream
            var aisSection = root["AisStream"]?.AsObject() ?? new JsonObject();
            aisSection["ApiKey"] = AisStreamApiKey;
            root["AisStream"] = aisSection;

            // Datalastic
            var datalasticSection = root["Datalastic"]?.AsObject() ?? new JsonObject();
            datalasticSection["ApiKey"] = DatalasticApiKey;
            datalasticSection["PollIntervalSeconds"] = DatalasticPollInterval;
            root["Datalastic"] = datalasticSection;

            // DataDocked
            var dataDockedSection = root["DataDocked"]?.AsObject() ?? new JsonObject();
            dataDockedSection["ApiKey"] = DataDockedApiKey;
            dataDockedSection["PollIntervalSeconds"] = DataDockedPollInterval;
            root["DataDocked"] = dataDockedSection;

            // VesselFinder
            var vfSection = root["VesselFinder"]?.AsObject() ?? new JsonObject();
            vfSection["ApiKey"] = VesselFinderApiKey;
            root["VesselFinder"] = vfSection;

            File.WriteAllText(path, root.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
