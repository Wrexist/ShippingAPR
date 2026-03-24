using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.AisStream.Messages;

namespace ShippingAPR.Infrastructure.Mapping;

public sealed class AisMessageMapper
{
    private readonly ILogger<AisMessageMapper> _logger;

    public AisMessageMapper(ILogger<AisMessageMapper> logger)
    {
        _logger = logger;
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

        return new AisMessageEventArgs
        {
            MessageType = "PositionReport",
            Mmsi = mmsi,
            Position = new VesselPosition
            {
                Latitude = message.MetaData?.Latitude ?? data.Latitude,
                Longitude = message.MetaData?.Longitude ?? data.Longitude,
                SpeedOverGround = data.Sog,
                CourseOverGround = data.Cog,
                TrueHeading = data.TrueHeading == 511 ? data.Cog : data.TrueHeading,
                Status = (NavigationalStatus)Math.Min(data.NavigationalStatus, 15),
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
                Draught = data.MaximumStaticDraught / 10.0,
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
        // Source: ITU-R M.585 MID allocation table
        var mid = mmsi / 1_000_000;
        return mid switch
        {
            // Northern Europe
            265 or 266 => "SE",  // Sweden
            219 => "DK",         // Denmark
            257 => "NO",         // Norway
            230 or 231 => "FI",  // Finland
            220 => "DK",         // Denmark (additional)
            258 => "NO",         // Norway (additional)

            // Western Europe
            211 => "DE",         // Germany
            218 => "DE",         // Germany (additional)
            244 or 245 => "NL",  // Netherlands
            246 => "NL",         // Netherlands (additional)
            226 or 227 => "FR",  // France
            228 => "FR",         // France (additional)
            232 or 233 or 234 or 235 => "GB", // United Kingdom
            250 => "IE",         // Ireland
            205 or 206 => "BE",  // Belgium
            236 or 237 => "GI",  // Gibraltar

            // Southern Europe
            224 or 225 => "ES",  // Spain
            263 => "PT",         // Portugal
            247 => "IT",         // Italy
            237 or 239 => "GR",  // Greece
            240 => "GR",         // Greece (additional)
            256 => "MT",         // Malta
            249 => "MT",         // Malta (additional)
            278 => "HR",         // Croatia
            238 => "HR",         // Croatia (additional)

            // Eastern Europe
            261 or 262 => "PL",  // Poland
            271 => "TR",         // Turkey
            273 => "RU",         // Russia
            255 => "PT",         // Madeira/Portugal

            // Americas
            338 or 366 or 367 or 368 or 369 => "US", // United States
            316 => "CA",         // Canada
            345 => "MX",         // Mexico
            351 => "MX",         // Mexico (additional)
            311 => "BS",         // Bahamas
            309 => "BS",         // Bahamas (additional)
            312 or 314 => "BZ",  // Belize
            352 or 353 or 354 or 355 or 356 or 357 => "PA", // Panama
            370 or 371 or 372 or 373 or 374 => "PA", // Panama (additional)
            710 or 725 => "BR",  // Brazil
            701 => "AR",         // Argentina
            730 => "CO",         // Colombia

            // Asia
            412 or 413 or 414 => "CN",  // China
            431 or 432 => "JP",  // Japan
            440 or 441 => "KR",  // South Korea
            416 => "TW",         // Taiwan
            525 => "ID",         // Indonesia
            533 => "MY",         // Malaysia
            563 or 564 or 565 => "SG", // Singapore
            548 => "PH",         // Philippines
            567 => "TH",         // Thailand
            574 => "VN",         // Vietnam
            419 => "SA",         // Saudi Arabia
            470 => "AE",         // UAE

            // Oceania
            503 => "AU",         // Australia
            512 => "NZ",         // New Zealand

            // Africa
            601 => "ZA",         // South Africa
            622 => "EG",         // Egypt
            618 or 619 => "CI",  // Côte d'Ivoire
            625 => "ER",         // Eritrea

            // Convenience flags
            209 or 210 => "CY",  // Cyprus
            212 => "CY",         // Cyprus (additional)
            572 => "VU",         // Vanuatu
            375 or 376 or 377 => "VC", // Saint Vincent
            341 => "KN",         // Saint Kitts and Nevis

            // Marshall Islands (major flag state)
            538 => "MH",         // Marshall Islands

            // Liberia (major flag state)
            636 or 637 => "LR",  // Liberia

            // Hong Kong
            477 => "HK",         // Hong Kong

            _ => null
        };
    }
}
