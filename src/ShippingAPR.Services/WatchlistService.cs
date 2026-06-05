using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

using ShippingAPR.Core.IO;

namespace ShippingAPR.Services;

public sealed class WatchlistService : IWatchlistService, IDisposable
{
    private static readonly string WatchlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShippingAPR",
        "watchlist.json");

    private readonly IVesselStore _vesselStore;
    private readonly NotificationService _notificationService;
    private readonly ILogger<WatchlistService> _logger;
    private readonly HashSet<int> _watched = [];
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<int, bool> _notifiedAppearance = new();

    public event EventHandler<int>? WatchlistChanged;

    public IReadOnlyCollection<int> WatchedMmsis
    {
        get { lock (_lock) return _watched.ToHashSet(); }
    }

    public WatchlistService(
        IVesselStore vesselStore,
        NotificationService notificationService,
        ILogger<WatchlistService> logger)
    {
        _vesselStore = vesselStore;
        _notificationService = notificationService;
        _logger = logger;

        Load();

        _vesselStore.VesselAdded += OnVesselAdded;
        _vesselStore.VesselUpdated += OnVesselUpdated;
    }

    public bool IsWatched(int mmsi)
    {
        lock (_lock) return _watched.Contains(mmsi);
    }

    public void Add(int mmsi)
    {
        lock (_lock)
        {
            if (!_watched.Add(mmsi)) return;
        }
        Save();
        WatchlistChanged?.Invoke(this, mmsi);
        _logger.LogInformation("Added MMSI {Mmsi} to watchlist", mmsi);
    }

    public void Remove(int mmsi)
    {
        lock (_lock)
        {
            if (!_watched.Remove(mmsi)) return;
        }
        _notifiedAppearance.TryRemove(mmsi, out _);
        Save();
        WatchlistChanged?.Invoke(this, mmsi);
        _logger.LogInformation("Removed MMSI {Mmsi} from watchlist", mmsi);
    }

    public void Toggle(int mmsi)
    {
        if (IsWatched(mmsi))
            Remove(mmsi);
        else
            Add(mmsi);
    }

    private void OnVesselAdded(object? sender, Vessel vessel)
    {
        if (!IsWatched(vessel.Mmsi)) return;

        // Notify on first appearance of watched vessel
        if (_notifiedAppearance.TryAdd(vessel.Mmsi, true))
        {
            _notificationService.Publish(new NotificationMessage
            {
                Title = "Watched Vessel Detected",
                Body = $"{vessel.DisplayName} (MMSI {vessel.Mmsi}) appeared",
                Type = NotificationType.WatchlistAlert
            });
        }
    }

    private void OnVesselUpdated(object? sender, Vessel vessel)
    {
        if (!IsWatched(vessel.Mmsi)) return;

        // Notify on first appearance of watched vessel (in case it was already in store before watch)
        if (_notifiedAppearance.TryAdd(vessel.Mmsi, true))
        {
            _notificationService.Publish(new NotificationMessage
            {
                Title = "Watched Vessel Detected",
                Body = $"{vessel.DisplayName} (MMSI {vessel.Mmsi}) is being tracked",
                Type = NotificationType.WatchlistAlert
            });
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(WatchlistPath)) return;

            var json = File.ReadAllText(WatchlistPath);
            var mmsis = JsonSerializer.Deserialize<int[]>(json);
            if (mmsis is null) return;

            lock (_lock)
            {
                foreach (var mmsi in mmsis)
                    _watched.Add(mmsi);
            }
            _logger.LogDebug("Loaded {Count} vessels from watchlist", mmsis.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load watchlist, starting empty");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(WatchlistPath)!;
            Directory.CreateDirectory(dir);

            int[] mmsis;
            lock (_lock) { mmsis = [.. _watched]; }

            var json = JsonSerializer.Serialize(mmsis, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteAllText(WatchlistPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save watchlist");
        }
    }

    public void Dispose()
    {
        _vesselStore.VesselAdded -= OnVesselAdded;
        _vesselStore.VesselUpdated -= OnVesselUpdated;
    }
}
