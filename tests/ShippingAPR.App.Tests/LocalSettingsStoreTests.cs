using System.Text.Json.Nodes;
using FluentAssertions;
using ShippingAPR.App.Configuration;
using Xunit;

namespace ShippingAPR.App.Tests;

public class LocalSettingsStoreTests
{
    [Fact]
    public void SetSectionValue_CreatesMissingSection()
    {
        var root = new JsonObject();

        LocalSettingsStore.SetSectionValue(root, "AisStream", "ApiKey", "secret");

        root["AisStream"]!["ApiKey"]!.GetValue<string>().Should().Be("secret");
    }

    [Fact]
    public void SetSectionValue_UpdatesExistingSectionInPlace()
    {
        var root = new JsonObject
        {
            ["AisStream"] = new JsonObject { ["WebSocketUrl"] = "wss://example", ["ApiKey"] = "old" }
        };

        LocalSettingsStore.SetSectionValue(root, "AisStream", "ApiKey", "new");

        root["AisStream"]!["ApiKey"]!.GetValue<string>().Should().Be("new");
        // Sibling keys in the same section are preserved.
        root["AisStream"]!["WebSocketUrl"]!.GetValue<string>().Should().Be("wss://example");
    }

    [Fact]
    public void SetSectionValue_PreservesOtherSections()
    {
        var root = new JsonObject
        {
            ["Datalastic"] = new JsonObject { ["ApiKey"] = "keep" }
        };

        LocalSettingsStore.SetSectionValue(root, "AisStream", "ApiKey", "secret");

        root["Datalastic"]!["ApiKey"]!.GetValue<string>().Should().Be("keep");
        root["AisStream"]!["ApiKey"]!.GetValue<string>().Should().Be("secret");
    }

    [Fact]
    public void SetSectionValue_SupportsNumericValues()
    {
        var root = new JsonObject();

        LocalSettingsStore.SetSectionValue(root, "Datalastic", "PollIntervalSeconds", 15);

        root["Datalastic"]!["PollIntervalSeconds"]!.GetValue<int>().Should().Be(15);
    }

    [Fact]
    public void FilePath_IsUnderApplicationData_NotInstallDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        LocalSettingsStore.FilePath.Should().StartWith(appData);
        LocalSettingsStore.FilePath.Should().EndWith("appsettings.Local.json");
    }
}
