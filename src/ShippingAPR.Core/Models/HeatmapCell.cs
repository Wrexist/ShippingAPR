namespace ShippingAPR.Core.Models;

public sealed class HeatmapCell
{
    public int GridX { get; init; }
    public int GridY { get; init; }
    public double CenterLat { get; init; }
    public double CenterLon { get; init; }
    public int Intensity { get; set; }
}

public sealed class HeatmapGrid
{
    public required double MinLat { get; init; }
    public required double MaxLat { get; init; }
    public required double MinLon { get; init; }
    public required double MaxLon { get; init; }
    public required int Resolution { get; init; }
    public required HeatmapCell[,] Cells { get; init; }

    public int MaxIntensity { get; set; }

    public void AddPoint(double lat, double lon, int weight = 1)
    {
        var lonSpan = MaxLon - MinLon;
        var latSpan = MaxLat - MinLat;
        if (lonSpan <= 0 || latSpan <= 0 || weight <= 0)
            return;

        var gridX = (int)((lon - MinLon) / lonSpan * Resolution);
        var gridY = (int)((lat - MinLat) / latSpan * Resolution);

        // Out of the box entirely → drop. A point exactly on the max edge maps to
        // index == Resolution; clamp it to the last cell rather than discarding it.
        if (gridX < 0 || gridY < 0 || gridX > Resolution || gridY > Resolution)
            return;
        if (gridX == Resolution) gridX = Resolution - 1;
        if (gridY == Resolution) gridY = Resolution - 1;

        Cells[gridY, gridX].Intensity += weight;
        if (Cells[gridY, gridX].Intensity > MaxIntensity)
            MaxIntensity = Cells[gridY, gridX].Intensity;
    }
}
