using System.Reflection;
using System.Text.Json;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Ports;

public sealed class PortRepository : IPortRepository
{
    private const double FuzzyMatchThreshold = 0.5;
    private const int MinLocodeLength = 4;
    private const int FullLocodeLength = 5;

    private readonly Lazy<(Dictionary<string, Port> ByLocode, List<Port> AllPorts)> _data;

    private Dictionary<string, Port> ByLocode => _data.Value.ByLocode;
    private List<Port> AllPorts => _data.Value.AllPorts;

    public PortRepository()
    {
        _data = new Lazy<(Dictionary<string, Port>, List<Port>)>(LoadPorts);
    }

    private static (Dictionary<string, Port> ByLocode, List<Port> AllPorts) LoadPorts()
    {
        var byLocode = new Dictionary<string, Port>(StringComparer.OrdinalIgnoreCase);
        var allPorts = new List<Port>();

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "ShippingAPR.Infrastructure.Ports.ports.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return (byLocode, allPorts); // Return empty — don't crash

        PortEntry[]? ports;
        try
        {
            ports = JsonSerializer.Deserialize<PortEntry[]>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return (byLocode, allPorts); // Return empty — don't crash
        }

        if (ports is null) return (byLocode, allPorts);

        foreach (var entry in ports)
        {
            var port = new Port(entry.Locode, entry.Name, entry.Country, entry.Lat, entry.Lon);
            byLocode[entry.Locode] = port;
            allPorts.Add(port);
        }

        return (byLocode, allPorts);
    }

    public Port? FindByLocode(string locode)
    {
        if (string.IsNullOrWhiteSpace(locode)) return null;

        // Try direct match
        if (ByLocode.TryGetValue(locode, out var port))
            return port;

        // Try with common prefixes/formats
        var cleaned = locode.Trim().ToUpperInvariant().Replace(" ", "");
        if (ByLocode.TryGetValue(cleaned, out port))
            return port;

        return null;
    }

    public Port? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var normalized = name.Trim().ToUpperInvariant();

        // Exact match first
        var exact = AllPorts.FirstOrDefault(p =>
            p.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Contains match
        var contains = AllPorts.FirstOrDefault(p =>
            p.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        if (contains is not null) return contains;

        // Fuzzy: try matching destination field which may contain partial port names
        return AllPorts
            .Select(p => new { Port = p, Score = FuzzyScore(p.Name, normalized) })
            .Where(x => x.Score > FuzzyMatchThreshold)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault()?.Port;
    }

    public Port? FindNearest(double latitude, double longitude, double maxDistanceNm = 100.0)
    {
        var nearest = AllPorts
            .Select(p => new
            {
                Port = p,
                Distance = HaversineCalculator.DistanceInNauticalMiles(
                    latitude, longitude, p.Latitude, p.Longitude)
            })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        // With a small port DB, the geometrically-nearest port can be hundreds of NM
        // away. Don't attribute a vessel to a port it isn't actually near.
        return nearest is not null && nearest.Distance <= maxDistanceNm ? nearest.Port : null;
    }

    public IEnumerable<Port> SearchPorts(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Enumerable.Empty<Port>();

        return AllPorts.Where(p =>
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Locode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Country.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<Port> GetAll() => AllPorts;

    /// <summary>
    /// Resolves an AIS destination string to a port. Tries LOCODE first,
    /// then name-based matching with common AIS abbreviations.
    /// </summary>
    public Port? ResolveDestination(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) return null;

        var cleaned = destination.Trim().ToUpperInvariant();

        // Try as UN/LOCODE (e.g., "SEGOT", "SE GOT")
        if (cleaned.Length >= MinLocodeLength)
        {
            var locode = cleaned.Replace(" ", "");
            var port = FindByLocode(locode);
            if (port is not null) return port;

            // Try just the city code with common country prefixes
            if (locode.Length >= FullLocodeLength)
            {
                port = FindByLocode(locode[..FullLocodeLength]);
                if (port is not null) return port;
            }
        }

        // Try as port name
        return FindByName(cleaned);
    }

    private static double FuzzyScore(string source, string target)
    {
        if (source.Length == 0 || target.Length == 0) return 0;

        var s = source.ToUpperInvariant();
        var t = target.ToUpperInvariant();

        if (s.StartsWith(t) || t.StartsWith(s))
            return 0.8 + (0.2 * Math.Min(s.Length, t.Length) / Math.Max(s.Length, t.Length));

        int matches = 0;
        int maxLen = Math.Min(s.Length, t.Length);
        for (int i = 0; i < maxLen; i++)
        {
            if (s[i] == t[i]) matches++;
        }

        return (double)matches / Math.Max(s.Length, t.Length);
    }

    private sealed record PortEntry(string Locode, string Name, string Country, double Lat, double Lon);
}
