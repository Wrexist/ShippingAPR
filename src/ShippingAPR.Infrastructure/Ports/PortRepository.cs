using System.Reflection;
using System.Text.Json;
using ShippingAPR.Core.Calculations;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.Ports;

public sealed class PortRepository : IPortRepository
{
    private readonly Dictionary<string, Port> _byLocode = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Port> _allPorts = [];

    public PortRepository()
    {
        LoadPorts();
    }

    private void LoadPorts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "ShippingAPR.Infrastructure.Ports.ports.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");

        PortEntry[]? ports;
        try
        {
            ports = JsonSerializer.Deserialize<PortEntry[]>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize embedded ports.json", ex);
        }

        if (ports is null) return;

        foreach (var entry in ports)
        {
            var port = new Port(entry.Locode, entry.Name, entry.Country, entry.Lat, entry.Lon);
            _byLocode[entry.Locode] = port;
            _allPorts.Add(port);
        }
    }

    public Port? FindByLocode(string locode)
    {
        if (string.IsNullOrWhiteSpace(locode)) return null;

        // Try direct match
        if (_byLocode.TryGetValue(locode, out var port))
            return port;

        // Try with common prefixes/formats
        var cleaned = locode.Trim().ToUpperInvariant().Replace(" ", "");
        if (_byLocode.TryGetValue(cleaned, out port))
            return port;

        return null;
    }

    public Port? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var normalized = name.Trim().ToUpperInvariant();

        // Exact match first
        var exact = _allPorts.FirstOrDefault(p =>
            p.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Contains match
        var contains = _allPorts.FirstOrDefault(p =>
            p.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        if (contains is not null) return contains;

        // Fuzzy: try matching destination field which may contain partial port names
        return _allPorts
            .Select(p => new { Port = p, Score = FuzzyScore(p.Name, normalized) })
            .Where(x => x.Score > 0.5)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault()?.Port;
    }

    public Port? FindNearest(double latitude, double longitude)
    {
        return _allPorts
            .Select(p => new
            {
                Port = p,
                Distance = HaversineCalculator.DistanceInNauticalMiles(
                    latitude, longitude, p.Latitude, p.Longitude)
            })
            .OrderBy(x => x.Distance)
            .FirstOrDefault()?.Port;
    }

    public IEnumerable<Port> SearchPorts(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Enumerable.Empty<Port>();

        return _allPorts.Where(p =>
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Locode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Country.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves an AIS destination string to a port. Tries LOCODE first,
    /// then name-based matching with common AIS abbreviations.
    /// </summary>
    public Port? ResolveDestination(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) return null;

        var cleaned = destination.Trim().ToUpperInvariant();

        // Try as UN/LOCODE (e.g., "SEGOT", "SE GOT")
        if (cleaned.Length >= 4)
        {
            var locode = cleaned.Replace(" ", "");
            var port = FindByLocode(locode);
            if (port is not null) return port;

            // Try just the city code with common country prefixes
            if (locode.Length >= 5)
            {
                port = FindByLocode(locode[..5]);
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
