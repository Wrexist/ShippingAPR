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

    public void AddPoint(double lat, double lon)
    {
        var gridX = (int)((lon - MinLon) / (MaxLon - MinLon) * Resolution);
        var gridY = (int)((lat - MinLat) / (MaxLat - MinLat) * Resolution);

        if (gridX < 0 || gridX >= Resolution || gridY < 0 || gridY >= Resolution)
            return;

        Cells[gridY, gridX].Intensity++;
        if (Cells[gridY, gridX].Intensity > MaxIntensity)
            MaxIntensity = Cells[gridY, gridX].Intensity;
    }
}
