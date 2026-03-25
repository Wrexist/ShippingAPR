namespace ShippingAPR.Core.Models;

/// <summary>
/// A user-defined or auto-generated fleet grouping.
/// </summary>
public sealed class FleetGroup
{
    public required string Name { get; init; }
    public string Color { get; init; } = "#6C63FF";
    public FleetGroupType GroupType { get; init; } = FleetGroupType.Custom;
    public List<int> MmsiList { get; init; } = [];
}

public enum FleetGroupType
{
    Custom,
    ByFlag,
    ByType
}
