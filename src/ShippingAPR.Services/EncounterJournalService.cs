using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Auto-populates a ship-spotter's encounter journal, logging each unique vessel
/// sighting with details, weather context, and user-editable notes/ratings.
/// </summary>
public sealed class EncounterJournalService : IDisposable
{
    private readonly IVesselStore _vesselStore;
    private readonly ILogger<EncounterJournalService> _logger;
    private readonly ConcurrentDictionary<int, bool> _seenMmsis = new();
    private readonly List<Encounter> _encounters = [];
    private readonly object _lock = new();

    private const int MaxEncounters = 5000;
    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR", "encounter-journal.json");

    public event EventHandler<Encounter>? EncounterAdded;

    public EncounterJournalService(IVesselStore vesselStore, ILogger<EncounterJournalService> logger)
    {
        _vesselStore = vesselStore;
        _logger = logger;

        Load();

        _vesselStore.VesselAdded += OnVesselAdded;
    }

    private void OnVesselAdded(object? sender, Vessel vessel)
    {
        // Only log first sighting per session (unique MMSI)
        if (!_seenMmsis.TryAdd(vessel.Mmsi, true))
            return;

        var encounter = new Encounter
        {
            Mmsi = vessel.Mmsi,
            VesselName = vessel.DisplayName,
            VesselType = vessel.Type,
            Latitude = vessel.CurrentPosition?.Latitude ?? 0,
            Longitude = vessel.CurrentPosition?.Longitude ?? 0,
            SpeedKnots = vessel.CurrentPosition?.SpeedOverGround ?? 0,
            Destination = vessel.StaticData?.Destination,
            CountryCode = vessel.StaticData?.CountryCode,
            LengthOverall = vessel.StaticData?.LengthOverall ?? 0,
            Timestamp = DateTime.UtcNow
        };

        lock (_lock)
        {
            _encounters.Add(encounter);
            if (_encounters.Count > MaxEncounters)
                _encounters.RemoveAt(0);
        }

        EncounterAdded?.Invoke(this, encounter);
        _logger.LogDebug("New encounter: {Vessel} (MMSI {Mmsi})", vessel.DisplayName, vessel.Mmsi);

        // Auto-save periodically (every 50 encounters), non-blocking
        if (_encounters.Count % 50 == 0)
            Task.Run(Save);
    }

    public IReadOnlyList<Encounter> GetAll()
    {
        lock (_lock) { return _encounters.ToList().AsReadOnly(); }
    }

    public IReadOnlyList<Encounter> Search(string query, int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAll();

        lock (_lock)
        {
            return _encounters
                .Where(e =>
                    e.VesselName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Mmsi.ToString().Contains(query) ||
                    (e.CountryCode?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Destination?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    e.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .TakeLast(maxResults)
                .Reverse()
                .ToList();
        }
    }

    public void UpdateNotes(string encounterId, string notes)
    {
        lock (_lock)
        {
            var encounter = _encounters.FirstOrDefault(e => e.Id == encounterId);
            if (encounter is not null)
            {
                encounter.UserNotes = notes;
                Save();
            }
        }
    }

    public void UpdateRating(string encounterId, int rating)
    {
        lock (_lock)
        {
            var encounter = _encounters.FirstOrDefault(e => e.Id == encounterId);
            if (encounter is not null)
            {
                encounter.Rating = Math.Clamp(rating, 0, 5);
                Save();
            }
        }
    }

    public JournalStats GetStats()
    {
        lock (_lock)
        {
            if (_encounters.Count == 0)
                return new JournalStats();

            var countries = _encounters
                .Where(e => !string.IsNullOrEmpty(e.CountryCode))
                .Select(e => e.CountryCode!)
                .Distinct()
                .ToList();

            var types = _encounters
                .Select(e => e.VesselType)
                .Distinct()
                .ToList();

            // Find rarest type (lowest count)
            var typeCounts = _encounters
                .GroupBy(e => e.VesselType)
                .OrderBy(g => g.Count())
                .ToList();

            var mostSpottedCountry = _encounters
                .Where(e => !string.IsNullOrEmpty(e.CountryCode))
                .GroupBy(e => e.CountryCode!)
                .MaxBy(g => g.Count())?.Key;

            return new JournalStats
            {
                TotalEncounters = _encounters.Count,
                UniqueCountries = countries.Count,
                UniqueTypes = types.Count,
                RarestTypeName = typeCounts.FirstOrDefault()?.Key.ToString(),
                MostSpottedCountry = mostSpottedCountry,
                FirstEncounterDate = _encounters.FirstOrDefault()?.Timestamp,
                LatestEncounterDate = _encounters.LastOrDefault()?.Timestamp
            };
        }
    }

    public string ExportToCsv()
    {
        lock (_lock)
        {
            var lines = new List<string>
            {
                "Timestamp,MMSI,Name,Type,Country,Speed,Lat,Lon,Destination,Rating,Notes"
            };

            foreach (var e in _encounters)
            {
                var notes = e.UserNotes.Replace("\"", "\"\"");
                lines.Add($"{e.Timestamp:yyyy-MM-dd HH:mm:ss},{e.Mmsi},\"{e.VesselName}\",{e.VesselType},{e.CountryCode ?? ""}," +
                          $"{e.SpeedKnots:F1},{e.Latitude:F4},{e.Longitude:F4},\"{e.Destination ?? ""}\",{e.Rating},\"{notes}\"");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    internal void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(DataPath)!;
            Directory.CreateDirectory(dir);
            List<Encounter> copy;
            lock (_lock) { copy = _encounters.ToList(); }
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save encounter journal");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(DataPath)) return;
            var json = File.ReadAllText(DataPath);
            var encounters = JsonSerializer.Deserialize<List<Encounter>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (encounters is not null)
            {
                lock (_lock) { _encounters.AddRange(encounters); }
                foreach (var e in encounters)
                    _seenMmsis.TryAdd(e.Mmsi, true);
                _logger.LogInformation("Loaded {Count} encounters from journal", encounters.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load encounter journal");
        }
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselAdded;
        Save();
    }
}
