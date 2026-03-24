using System.Globalization;
using System.Text;
using System.Text.Json;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Services;

public sealed class ExportService
{
    private readonly IVesselStore _vesselStore;

    public ExportService(IVesselStore vesselStore)
    {
        _vesselStore = vesselStore;
    }

    /// <summary>
    /// Exports the current vessel list to CSV format.
    /// </summary>
    public string ExportToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,MMSI,Type,Latitude,Longitude,Speed (kn),Course,Heading,Destination,ETA,Flag");

        foreach (var vessel in _vesselStore.Vessels.Values)
        {
            var name = Escape(vessel.StaticData?.Name ?? "Unknown");
            var type = vessel.StaticData?.ShipType.ToString() ?? "Unknown";
            var lat = vessel.CurrentPosition?.Latitude.ToString("F5", CultureInfo.InvariantCulture) ?? "";
            var lon = vessel.CurrentPosition?.Longitude.ToString("F5", CultureInfo.InvariantCulture) ?? "";
            var speed = vessel.CurrentPosition?.SpeedOverGround.ToString("F1", CultureInfo.InvariantCulture) ?? "";
            var course = vessel.CurrentPosition?.CourseOverGround.ToString("F1", CultureInfo.InvariantCulture) ?? "";
            var heading = vessel.CurrentPosition?.TrueHeading.ToString("F1", CultureInfo.InvariantCulture) ?? "";
            var destination = Escape(vessel.StaticData?.Destination ?? "");
            var eta = vessel.CalculatedEta?.EstimatedArrival.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";
            var flag = vessel.StaticData?.CountryCode ?? "";

            sb.AppendLine($"{name},{vessel.Mmsi},{type},{lat},{lon},{speed},{course},{heading},{destination},{eta},{flag}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports the current vessel list to JSON format.
    /// </summary>
    public string ExportToJson()
    {
        var vessels = _vesselStore.Vessels.Values.Select(v => new
        {
            v.Mmsi,
            Name = v.StaticData?.Name ?? "Unknown",
            Type = v.StaticData?.ShipType.ToString() ?? "Unknown",
            Latitude = v.CurrentPosition?.Latitude,
            Longitude = v.CurrentPosition?.Longitude,
            SpeedKnots = v.CurrentPosition?.SpeedOverGround,
            Course = v.CurrentPosition?.CourseOverGround,
            Heading = v.CurrentPosition?.TrueHeading,
            Destination = v.StaticData?.Destination,
            ETA = v.CalculatedEta?.EstimatedArrival,
            Flag = v.StaticData?.CountryCode,
            LastUpdated = v.LastUpdated
        }).ToArray();

        return JsonSerializer.Serialize(vessels, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
