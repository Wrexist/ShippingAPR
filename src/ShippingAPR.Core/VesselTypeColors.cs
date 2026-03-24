using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core;

/// <summary>
/// Single source of truth for vessel type color mappings (R, G, B).
/// Used by both map rendering and UI converters.
/// </summary>
public static class VesselTypeColors
{
    public static (byte R, byte G, byte B) GetRgb(VesselType type) => type switch
    {
        VesselType.Cargo => (76, 175, 80),
        VesselType.Tanker => (255, 87, 34),
        VesselType.Passenger => (33, 150, 243),
        VesselType.Fishing => (255, 152, 0),
        VesselType.Tug or VesselType.Pilot => (156, 39, 176),
        VesselType.Military => (96, 125, 139),
        VesselType.Sailing or VesselType.PleasureCraft => (0, 188, 212),
        VesselType.HighSpeedCraft => (255, 235, 59),
        VesselType.SearchAndRescue => (244, 67, 54),
        _ => (158, 158, 158)
    };
}
