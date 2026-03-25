using ShippingAPR.Core.Enums;

namespace ShippingAPR.Core;

/// <summary>
/// Single source of truth for vessel type color mappings (R, G, B).
/// Used by both map rendering and UI converters.
/// Colors are stored in a dictionary for easy runtime override.
/// </summary>
public static class VesselTypeColors
{
    private static readonly Dictionary<VesselType, (byte R, byte G, byte B)> DefaultColors = new()
    {
        [VesselType.Cargo] = (76, 175, 80),
        [VesselType.Tanker] = (255, 87, 34),
        [VesselType.Passenger] = (33, 150, 243),
        [VesselType.Fishing] = (255, 152, 0),
        [VesselType.Tug] = (156, 39, 176),
        [VesselType.Pilot] = (156, 39, 176),
        [VesselType.Military] = (96, 125, 139),
        [VesselType.Sailing] = (0, 188, 212),
        [VesselType.PleasureCraft] = (0, 188, 212),
        [VesselType.HighSpeedCraft] = (255, 235, 59),
        [VesselType.SearchAndRescue] = (244, 67, 54),
    };

    private static readonly (byte R, byte G, byte B) UnknownColor = (158, 158, 158);

    public static (byte R, byte G, byte B) GetRgb(VesselType type) =>
        DefaultColors.TryGetValue(type, out var color) ? color : UnknownColor;
}
