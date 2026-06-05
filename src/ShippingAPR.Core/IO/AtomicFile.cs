using System.IO;

namespace ShippingAPR.Core.IO;

/// <summary>
/// File helpers that write atomically so a crash or power loss mid-write cannot
/// corrupt (or truncate to empty) an existing file — important for the small JSON
/// persistence files holding the user's watchlist, fleets, alerts and preferences.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="contents"/> to a temporary file in the same directory and
    /// then moves it over <paramref name="path"/>, which is atomic on the same volume.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
