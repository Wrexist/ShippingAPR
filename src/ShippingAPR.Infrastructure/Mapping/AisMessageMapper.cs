using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Constants;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.AisStream.Messages;

namespace ShippingAPR.Infrastructure.Mapping;

public sealed class AisMessageMapper
{

    private static readonly Dictionary<int, string> MidToCountryCode = LoadMmsiCountryMappings();

    private readonly ILogger<AisMessageMapper> _logger;

    public AisMessageMapper(ILogger<AisMessageMapper> logger)
    {
        _logger = logger;
    }

    private static Dictionary<int, string> LoadMmsiCountryMappings()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "ShippingAPR.Infrastructure.Mapping.mmsi-countries.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return new Dictionary<int, string>();

        using var doc = JsonDocument.Parse(stream);
        var mappings = new Dictionary<int, string>();

        if (doc.RootElement.TryGetProperty("mappings", out var arr))
        {
            foreach (var entry in arr.EnumerateArray())
            {
                if (entry.TryGetProperty("mid", out var mid) &&
                    entry.TryGetProperty("countryCode", out var cc))
                {
                    mappings[mid.GetInt32()] = cc.GetString()!;
                }
            }
        }

        return mappings;
    }

    public AisMessageEventArgs? Map(AisMessage message)
    {
        return message.MessageType switch
        {
            "PositionReport" => MapPositionReport(message),
            "ShipStaticData" => MapStaticData(message),
            _ => null
        };
    }

    private static AisMessageEventArgs? MapPositionReport(AisMessage message)
    {
        var report = message.Message.Deserialize<PositionReportMessage>();
        var data = report?.PositionReport;
        if (data is null) return null;

        var mmsi = message.MetaData?.Mmsi ?? data.UserId;

        var latitude = message.MetaData?.Latitude ?? data.Latitude;
        var longitude = message.MetaData?.Longitude ?? data.Longitude;

        // AIS reports lat=91 / lon=181 when no position fix is available. Such a report
        // carries no usable position, so skip it rather than plotting the sentinel
        // (and rather than letting VesselPosition's range guard throw, which would be
        // swallowed upstream and silently drop every "no-fix" report).
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return null;

        // SOG 102.3 (raw 1023) and COG 360.0 (raw 3600) are AIS "not available" codes.
        // Normalise them to 0 so they are never displayed or used as real values.
        var sog = data.Sog >= NavigationConstants.SpeedOverGroundNotAvailable ? 0 : data.Sog;
        var cog = data.Cog >= NavigationConstants.CourseOverGroundNotAvailable ? 0 : data.Cog;

        return new AisMessageEventArgs
        {
            MessageType = "PositionReport",
            Mmsi = mmsi,
            Position = new VesselPosition
            {
                Latitude = latitude,
                Longitude = longitude,
                SpeedOverGround = sog,
                CourseOverGround = cog,
                TrueHeading = data.TrueHeading == NavigationConstants.TrueHeadingNotAvailable ? cog : data.TrueHeading,
                Status = (NavigationalStatus)Math.Min(data.NavigationalStatus, NavigationConstants.MaxNavigationalStatus),
                RateOfTurn = data.RateOfTurn,
                Timestamp = DateTime.UtcNow
            }
        };
    }

    private AisMessageEventArgs? MapStaticData(AisMessage message)
    {
        var report = message.Message.Deserialize<ShipStaticDataMessage>();
        var data = report?.ShipStaticData;
        if (data is null) return null;

        var mmsi = message.MetaData?.Mmsi ?? data.UserId;

        DateTime? reportedEta = null;
        if (data.Eta is { Month: > 0 and <= 12, Day: > 0 and <= 31 })
        {
            try
            {
                var year = DateTime.UtcNow.Year;
                reportedEta = new DateTime(year, data.Eta.Month, data.Eta.Day,
                    Math.Clamp(data.Eta.Hour, 0, 23),
                    Math.Clamp(data.Eta.Minute, 0, 59), 0, DateTimeKind.Utc);

                if (reportedEta < DateTime.UtcNow.AddDays(-1))
                    reportedEta = reportedEta.Value.AddYears(1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogDebug(ex, "Invalid ETA date from AIS for MMSI {Mmsi}", message.MetaData?.Mmsi);
            }
        }

        var shipType = MapShipType(data.Type);
        var countryCode = MmsiToCountryCode(mmsi);

        return new AisMessageEventArgs
        {
            MessageType = "ShipStaticData",
            Mmsi = mmsi,
            StaticData = new VesselStaticData
            {
                Name = CleanAisString(data.Name),
                CallSign = CleanAisString(data.CallSign),
                ImoNumber = data.ImoNumber,
                ShipType = shipType,
                Destination = CleanAisString(data.Destination),
                ReportedEta = reportedEta,
                Draught = Math.Max(0, data.MaximumStaticDraught) / NavigationConstants.DraughtDivisor,
                DimensionA = data.Dimension?.A ?? 0,
                DimensionB = data.Dimension?.B ?? 0,
                DimensionC = data.Dimension?.C ?? 0,
                DimensionD = data.Dimension?.D ?? 0,
                CountryCode = countryCode
            }
        };
    }

    private static VesselType MapShipType(int aisType) => aisType switch
    {
        >= 20 and < 30 => VesselType.WingInGround,
        30 => VesselType.Fishing,
        31 or 32 => VesselType.Towing,
        33 => VesselType.Dredging,
        34 => VesselType.Diving,
        35 => VesselType.Military,
        36 => VesselType.Sailing,
        37 => VesselType.PleasureCraft,
        >= 40 and < 50 => VesselType.HighSpeedCraft,
        50 => VesselType.Pilot,
        51 => VesselType.SearchAndRescue,
        52 => VesselType.Tug,
        53 => VesselType.PortTender,
        58 => VesselType.MedicalTransport,
        >= 60 and < 70 => VesselType.Passenger,
        >= 70 and < 80 => VesselType.Cargo,
        >= 80 and < 90 => VesselType.Tanker,
        >= 90 => VesselType.OtherType,
        _ => VesselType.Unknown
    };

    private static string? CleanAisString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        // AIS pads with @ characters
        var cleaned = input.Trim().TrimEnd('@').Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    private static string? MmsiToCountryCode(int mmsi)
    {
        // First 3 digits of MMSI = MID (Maritime Identification Digits)
        // Mappings loaded from embedded mmsi-countries.json (source: ITU-R M.585)
        var mid = mmsi / NavigationConstants.MmsiToMidDivisor;
        return MidToCountryCode.TryGetValue(mid, out var code) ? code : null;
    }
}
