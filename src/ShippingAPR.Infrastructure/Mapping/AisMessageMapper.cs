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
            "StandardClassBPositionReport" => MapStandardClassBPositionReport(message),
            "ExtendedClassBPositionReport" => MapExtendedClassBPositionReport(message),
            "StaticDataReport" => MapStaticDataReport(message),
            _ => null
        };
    }

    private static AisMessageEventArgs? MapPositionReport(AisMessage message)
    {
        var report = message.Message.Deserialize<PositionReportMessage>();
        var data = report?.PositionReport;
        if (data is null) return null;

        var position = BuildPosition(message, data.Latitude, data.Longitude,
            data.Sog, data.Cog, data.TrueHeading, data.NavigationalStatus, data.RateOfTurn);
        if (position is null) return null;

        return new AisMessageEventArgs
        {
            MessageType = "PositionReport",
            Mmsi = message.MetaData?.Mmsi ?? data.UserId,
            Position = position
        };
    }

    /// <summary>
    /// Builds a normalised <see cref="VesselPosition"/> from raw AIS fields, or null when
    /// no usable position fix is present. Shared by Class A and Class B position reports.
    /// </summary>
    private static VesselPosition? BuildPosition(
        AisMessage message, double dataLat, double dataLon,
        double sog, double cog, int trueHeading, int navStatus, double rateOfTurn)
    {
        var latitude = message.MetaData?.Latitude ?? dataLat;
        var longitude = message.MetaData?.Longitude ?? dataLon;

        // AIS reports lat=91 / lon=181 when no position fix is available — skip it
        // rather than plotting the sentinel (or letting VesselPosition's range guard throw).
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return null;

        // SOG 102.3 (raw 1023) and COG 360.0 (raw 3600) are "not available" codes.
        var s = sog >= NavigationConstants.SpeedOverGroundNotAvailable ? 0 : sog;
        var c = cog >= NavigationConstants.CourseOverGroundNotAvailable ? 0 : cog;

        return new VesselPosition
        {
            Latitude = latitude,
            Longitude = longitude,
            SpeedOverGround = s,
            CourseOverGround = c,
            TrueHeading = trueHeading == NavigationConstants.TrueHeadingNotAvailable ? c : trueHeading,
            Status = (NavigationalStatus)Math.Min(navStatus, NavigationConstants.MaxNavigationalStatus),
            RateOfTurn = rateOfTurn,
            Timestamp = DateTime.UtcNow
        };
    }

    private static AisMessageEventArgs? MapStandardClassBPositionReport(AisMessage message)
    {
        var data = message.Message.Deserialize<StandardClassBPositionReportMessage>()?.StandardClassBPositionReport;
        if (data is null) return null;

        // Class B has no navigational status field — mark it NotDefined(15) rather than
        // defaulting to 0 ("under way using engine"), which would mislabel every Class B vessel.
        var position = BuildPosition(message, data.Latitude, data.Longitude,
            data.Sog, data.Cog, data.TrueHeading, NavigationConstants.MaxNavigationalStatus, 0);
        if (position is null) return null;

        return new AisMessageEventArgs
        {
            MessageType = "StandardClassBPositionReport",
            Mmsi = message.MetaData?.Mmsi ?? data.UserId,
            Position = position
        };
    }

    private static AisMessageEventArgs? MapExtendedClassBPositionReport(AisMessage message)
    {
        var data = message.Message.Deserialize<ExtendedClassBPositionReportMessage>()?.ExtendedClassBPositionReport;
        if (data is null) return null;

        var position = BuildPosition(message, data.Latitude, data.Longitude,
            data.Sog, data.Cog, data.TrueHeading, NavigationConstants.MaxNavigationalStatus, 0);
        if (position is null) return null;

        // Extended Class B also carries name/type/dimensions.
        VesselStaticData? staticData = null;
        var name = CleanAisString(data.Name);
        if (name is not null || data.Type > 0)
        {
            staticData = new VesselStaticData
            {
                Name = name,
                ShipType = MapShipType(data.Type),
                DimensionA = data.Dimension?.A ?? 0,
                DimensionB = data.Dimension?.B ?? 0,
                DimensionC = data.Dimension?.C ?? 0,
                DimensionD = data.Dimension?.D ?? 0,
                CountryCode = MmsiToCountryCode(message.MetaData?.Mmsi ?? data.UserId)
            };
        }

        return new AisMessageEventArgs
        {
            MessageType = "ExtendedClassBPositionReport",
            Mmsi = message.MetaData?.Mmsi ?? data.UserId,
            Position = position,
            StaticData = staticData
        };
    }

    private static AisMessageEventArgs? MapStaticDataReport(AisMessage message)
    {
        var data = message.Message.Deserialize<StaticDataReportMessage>()?.StaticDataReport;
        if (data is null) return null;

        var mmsi = message.MetaData?.Mmsi ?? data.UserId;
        var name = CleanAisString(data.ReportA?.Name);
        var callSign = CleanAisString(data.ReportB?.CallSign);
        var shipType = data.ReportB is not null ? MapShipType(data.ReportB.ShipType) : VesselType.Unknown;

        // Nothing useful to record.
        if (name is null && callSign is null && shipType == VesselType.Unknown)
            return null;

        return new AisMessageEventArgs
        {
            MessageType = "StaticDataReport",
            Mmsi = mmsi,
            StaticData = new VesselStaticData
            {
                Name = name,
                CallSign = callSign,
                ShipType = shipType,
                DimensionA = data.ReportB?.Dimension?.A ?? 0,
                DimensionB = data.ReportB?.Dimension?.B ?? 0,
                DimensionC = data.ReportB?.Dimension?.C ?? 0,
                DimensionD = data.ReportB?.Dimension?.D ?? 0,
                CountryCode = MmsiToCountryCode(mmsi)
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
