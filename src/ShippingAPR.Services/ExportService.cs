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

    /// <summary>
    /// Exports a vessel's track history to GPX format.
    /// </summary>
    public string ExportTrackToGpx(Vessel vessel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<gpx version=\"1.1\" creator=\"ShippingAPR\"");
        sb.AppendLine("     xmlns=\"http://www.topografix.com/GPX/1/1\">");
        sb.AppendLine($"  <trk>");
        sb.AppendLine($"    <name>{EscapeXml(vessel.DisplayName)} (MMSI: {vessel.Mmsi})</name>");
        sb.AppendLine($"    <desc>Track history exported from ShippingAPR</desc>");
        sb.AppendLine($"    <trkseg>");

        foreach (var tp in vessel.Track)
        {
            var lat = tp.Latitude.ToString("F6", CultureInfo.InvariantCulture);
            var lon = tp.Longitude.ToString("F6", CultureInfo.InvariantCulture);
            var time = tp.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            sb.AppendLine($"      <trkpt lat=\"{lat}\" lon=\"{lon}\">");
            sb.AppendLine($"        <time>{time}</time>");
            sb.AppendLine($"        <speed>{tp.SpeedOverGround.ToString("F1", CultureInfo.InvariantCulture)}</speed>");
            sb.AppendLine($"      </trkpt>");
        }

        sb.AppendLine($"    </trkseg>");
        sb.AppendLine($"  </trk>");
        sb.AppendLine("</gpx>");
        return sb.ToString();
    }

    /// <summary>
    /// Exports a vessel's track history to KML format.
    /// </summary>
    public string ExportTrackToKml(Vessel vessel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
        sb.AppendLine("  <Document>");
        sb.AppendLine($"    <name>{EscapeXml(vessel.DisplayName)} (MMSI: {vessel.Mmsi})</name>");
        sb.AppendLine("    <Style id=\"trackLine\">");
        sb.AppendLine("      <LineStyle>");
        sb.AppendLine("        <color>ff0000ff</color>");
        sb.AppendLine("        <width>3</width>");
        sb.AppendLine("      </LineStyle>");
        sb.AppendLine("    </Style>");
        sb.AppendLine("    <Placemark>");
        sb.AppendLine($"      <name>Track: {EscapeXml(vessel.DisplayName)}</name>");
        sb.AppendLine("      <styleUrl>#trackLine</styleUrl>");
        sb.AppendLine("      <LineString>");
        sb.AppendLine("        <tessellate>1</tessellate>");
        sb.Append("        <coordinates>");

        var first = true;
        foreach (var tp in vessel.Track)
        {
            if (!first) sb.Append(' ');
            sb.Append(tp.Longitude.ToString("F6", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(tp.Latitude.ToString("F6", CultureInfo.InvariantCulture));
            sb.Append(",0");
            first = false;
        }

        sb.AppendLine("</coordinates>");
        sb.AppendLine("      </LineString>");
        sb.AppendLine("    </Placemark>");
        sb.AppendLine("  </Document>");
        sb.AppendLine("</kml>");
        return sb.ToString();
    }

    internal static string Escape(string value)
    {
        // Mitigate CSV/formula injection: AIS Name/Destination are untrusted broadcast
        // strings, and a field beginning with one of these characters is executed as a
        // formula by Excel/Sheets. Prefix with a single quote so it renders as text.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");
}
