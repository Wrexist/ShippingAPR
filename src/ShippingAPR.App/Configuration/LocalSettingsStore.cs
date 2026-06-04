using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShippingAPR.App.Configuration;

/// <summary>
/// Reads and writes the user's machine-specific settings (including API keys) in a
/// per-user file under %APPDATA%, keeping secrets out of the install directory (which
/// may be read-only, shared, or accidentally committed). The shipped appsettings.json
/// holds only blank defaults; this file is layered on top as an override.
/// </summary>
public static class LocalSettingsStore
{
    /// <summary>Full path to %APPDATA%/ShippingAPR/appsettings.Local.json.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShippingAPR", "appsettings.Local.json");

    /// <summary>
    /// Loads the local settings file (or an empty document), applies <paramref name="mutate"/>,
    /// and writes it back, creating the directory if needed. Returns false on any I/O error.
    /// </summary>
    public static bool Update(Action<JsonObject> mutate)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir is not null) Directory.CreateDirectory(dir);

            var root = File.Exists(FilePath)
                ? JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject() ?? new JsonObject()
                : new JsonObject();

            mutate(root);

            File.WriteAllText(FilePath, root.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            // Best-effort: a save failure is non-fatal (the user can edit the file directly).
            return false;
        }
    }

    /// <summary>
    /// Sets a value within a named configuration section, creating the section if it does
    /// not already exist. Mutates the existing section in place to avoid JSON parent conflicts.
    /// </summary>
    public static void SetSectionValue(JsonObject root, string section, string key, JsonNode? value)
    {
        if (root[section] is not JsonObject obj)
        {
            obj = new JsonObject();
            root[section] = obj;
        }
        obj[key] = value;
    }
}
