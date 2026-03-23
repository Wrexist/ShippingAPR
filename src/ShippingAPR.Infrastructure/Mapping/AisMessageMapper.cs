using System.Text.Json;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure.AisStream.Messages;

namespace ShippingAPR.Infrastructure.Mapping;

public sealed class AisMessageMapper
{
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

    private static AisMessageEventArgs? MapStaticData(AisMessage message)
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
            catch
            {
                // Invalid ETA data from AIS
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
        var mid = mmsi / 1_000_000;
        return mid switch
        {
            265 or 266 => "SE", // Sweden
            219 => "DK",        // Denmark
            257 => "NO",        // Norway
            230 or 231 => "FI", // Finland
            211 => "DE",        // Germany
            244 or 245 => "NL", // Netherlands
            226 or 227 => "FR", // France
            232 or 233 or 234 or 235 => "GB",
            338 or 366 or 367 or 368 or 369 => "US",
            _ => null
        };
    }
}
