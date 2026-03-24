using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ShippingAPR.App.Configuration;

/// <summary>
/// Persists user preferences (theme, language, window state, filters)
/// to a JSON file in the app's local data folder.
/// </summary>
public sealed class UserPreferences
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR",
        "user-preferences.json");

    private readonly ILogger<UserPreferences> _logger;

    public bool IsDarkTheme { get; set; } = true;
    public string Language { get; set; } = "en";
    public double WindowWidth { get; set; } = 1400;
    public double WindowHeight { get; set; } = 900;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool ShowCargo { get; set; } = true;
    public bool ShowTanker { get; set; } = true;
    public bool ShowPassenger { get; set; } = true;
    public bool ShowFishing { get; set; } = true;
    public bool ShowTugPilot { get; set; } = true;
    public bool ShowOther { get; set; } = true;
    public double MaxSpeed { get; set; } = 50;

    public UserPreferences(ILogger<UserPreferences> logger)
    {
        _logger = logger;
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
                return;

            var json = File.ReadAllText(PreferencesPath);
            var data = JsonSerializer.Deserialize<PreferencesData>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data is null) return;

            IsDarkTheme = data.IsDarkTheme;
            Language = data.Language ?? "en";
            WindowWidth = data.WindowWidth > 0 ? data.WindowWidth : 1400;
            WindowHeight = data.WindowHeight > 0 ? data.WindowHeight : 900;
            WindowLeft = data.WindowLeft;
            WindowTop = data.WindowTop;
            ShowCargo = data.ShowCargo;
            ShowTanker = data.ShowTanker;
            ShowPassenger = data.ShowPassenger;
            ShowFishing = data.ShowFishing;
            ShowTugPilot = data.ShowTugPilot;
            ShowOther = data.ShowOther;
            MaxSpeed = data.MaxSpeed > 0 ? data.MaxSpeed : 50;

            _logger.LogDebug("Loaded user preferences from {Path}", PreferencesPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load user preferences, using defaults");
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(PreferencesPath)!;
            Directory.CreateDirectory(dir);

            var data = new PreferencesData
            {
                IsDarkTheme = IsDarkTheme,
                Language = Language,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                WindowLeft = WindowLeft,
                WindowTop = WindowTop,
                ShowCargo = ShowCargo,
                ShowTanker = ShowTanker,
                ShowPassenger = ShowPassenger,
                ShowFishing = ShowFishing,
                ShowTugPilot = ShowTugPilot,
                ShowOther = ShowOther,
                MaxSpeed = MaxSpeed
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PreferencesPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save user preferences");
        }
    }

    private sealed class PreferencesData
    {
        public bool IsDarkTheme { get; set; } = true;
        public string? Language { get; set; } = "en";
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 900;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public bool ShowCargo { get; set; } = true;
        public bool ShowTanker { get; set; } = true;
        public bool ShowPassenger { get; set; } = true;
        public bool ShowFishing { get; set; } = true;
        public bool ShowTugPilot { get; set; } = true;
        public bool ShowOther { get; set; } = true;
        public double MaxSpeed { get; set; } = 50;
    }
}
