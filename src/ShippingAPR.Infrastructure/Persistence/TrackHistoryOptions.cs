using System.IO;

namespace ShippingAPR.Infrastructure.Persistence;

public sealed class TrackHistoryOptions
{
    public const string SectionName = "TrackHistory";

    /// <summary>Path to the SQLite database file. Defaults to %LOCALAPPDATA%/ShippingAPR/history.db.</summary>
    public string DatabasePath { get; set; } = DefaultPath();

    /// <summary>How many days of track history to retain before purging.</summary>
    public int RetentionDays { get; set; } = 7;

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR", "history.db");
}
