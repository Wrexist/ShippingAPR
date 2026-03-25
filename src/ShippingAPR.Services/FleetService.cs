using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

/// <summary>
/// Manages fleet groupings - custom groups or auto-grouped by flag/type.
/// </summary>
public sealed class FleetService
{
    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR",
        "fleets.json");

    private readonly IVesselStore _vesselStore;
    private readonly ILogger<FleetService> _logger;
    private readonly List<FleetGroup> _groups = [];
    private readonly object _lock = new();

    public event EventHandler? GroupsChanged;

    public IReadOnlyList<FleetGroup> Groups
    {
        get { lock (_lock) return _groups.ToList(); }
    }

    public FleetService(IVesselStore vesselStore, ILogger<FleetService> logger)
    {
        _vesselStore = vesselStore;
        _logger = logger;
        Load();
    }

    public void AddGroup(FleetGroup group)
    {
        lock (_lock) _groups.Add(group);
        Save();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveGroup(string name)
    {
        lock (_lock) _groups.RemoveAll(g => g.Name == name);
        Save();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Auto-group all tracked vessels by flag country.</summary>
    public IReadOnlyList<FleetGroup> GetAutoGroupsByFlag()
    {
        return _vesselStore.Vessels.Values
            .Where(v => v.StaticData?.CountryCode is { Length: > 0 })
            .GroupBy(v => v.StaticData!.CountryCode!)
            .Select(g => new FleetGroup
            {
                Name = $"Flag: {g.Key}",
                GroupType = FleetGroupType.ByFlag,
                MmsiList = g.Select(v => v.Mmsi).ToList()
            })
            .OrderByDescending(g => g.MmsiList.Count)
            .ToList();
    }

    /// <summary>Auto-group all tracked vessels by type.</summary>
    public IReadOnlyList<FleetGroup> GetAutoGroupsByType()
    {
        return _vesselStore.Vessels.Values
            .GroupBy(v => v.Type)
            .Select(g => new FleetGroup
            {
                Name = g.Key.ToString(),
                GroupType = FleetGroupType.ByType,
                MmsiList = g.Select(v => v.Mmsi).ToList()
            })
            .OrderByDescending(g => g.MmsiList.Count)
            .ToList();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(DataPath)) return;
            var json = File.ReadAllText(DataPath);
            var groups = JsonSerializer.Deserialize<List<FleetGroup>>(json);
            if (groups is not null)
                lock (_lock) _groups.AddRange(groups);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load fleet groups");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(DataPath)!;
            Directory.CreateDirectory(dir);
            List<FleetGroup> custom;
            lock (_lock) custom = _groups.Where(g => g.GroupType == FleetGroupType.Custom).ToList();
            var json = JsonSerializer.Serialize(custom, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save fleet groups");
        }
    }
}
