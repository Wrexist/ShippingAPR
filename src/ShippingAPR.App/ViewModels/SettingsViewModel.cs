using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using ShippingAPR.App.Configuration;
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

    public AisProviderType[] AvailableProviders { get; } =
        [AisProviderType.AisStream, AisProviderType.Datalastic, AisProviderType.DataDocked];
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

    internal bool SaveToAppsettings() =>
        LocalSettingsStore.Update(root =>
        {
            LocalSettingsStore.SetSectionValue(root, "AisProvider", "Active", ActiveProvider.ToString());
            LocalSettingsStore.SetSectionValue(root, "AisProvider", "Fallback", FallbackProvider);
            LocalSettingsStore.SetSectionValue(root, "AisStream", "ApiKey", AisStreamApiKey);
            LocalSettingsStore.SetSectionValue(root, "Datalastic", "ApiKey", DatalasticApiKey);
            LocalSettingsStore.SetSectionValue(root, "Datalastic", "PollIntervalSeconds", DatalasticPollInterval);
            LocalSettingsStore.SetSectionValue(root, "DataDocked", "ApiKey", DataDockedApiKey);
            LocalSettingsStore.SetSectionValue(root, "DataDocked", "PollIntervalSeconds", DataDockedPollInterval);
            LocalSettingsStore.SetSectionValue(root, "VesselFinder", "ApiKey", VesselFinderApiKey);
        });
}
