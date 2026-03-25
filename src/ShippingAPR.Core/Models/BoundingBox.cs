namespace ShippingAPR.Core.Models;

public sealed record BoundingBox
{
    public double MinLatitude { get; }
    public double MinLongitude { get; }
    public double MaxLatitude { get; }
    public double MaxLongitude { get; }

    public BoundingBox(double MinLatitude, double MinLongitude, double MaxLatitude, double MaxLongitude)
    {
        if (MinLatitude > MaxLatitude)
            throw new ArgumentException($"MinLatitude ({MinLatitude}) must be <= MaxLatitude ({MaxLatitude})");
        if (MinLongitude > MaxLongitude)
            throw new ArgumentException($"MinLongitude ({MinLongitude}) must be <= MaxLongitude ({MaxLongitude})");
        if (MinLatitude is < -90 or > 90 || MaxLatitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(MinLatitude), "Latitude must be between -90 and 90");
        if (MinLongitude is < -180 or > 180 || MaxLongitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(MinLongitude), "Longitude must be between -180 and 180");

        this.MinLatitude = MinLatitude;
        this.MinLongitude = MinLongitude;
        this.MaxLatitude = MaxLatitude;
        this.MaxLongitude = MaxLongitude;
    }

    public bool Contains(double latitude, double longitude) =>
        latitude >= MinLatitude && latitude <= MaxLatitude &&
        longitude >= MinLongitude && longitude <= MaxLongitude;

    /// <summary>Gothenburg harbor.</summary>
    public static BoundingBox GothenburgDefault =>
        new(57.65, 11.80, 57.75, 12.05);

    /// <summary>Northern Europe — busy shipping lanes (English Channel, North Sea, Baltic).</summary>
    public static BoundingBox NorthernEuropeDefault =>
        new(48.0, -5.0, 62.0, 30.0);

    /// <summary>Major world shipping regions for global default view.</summary>
    public static BoundingBox[] WorldRegions =>
    [
        new(48.0, -5.0, 62.0, 30.0),    // Northern Europe
        new(0.0, 100.0, 10.0, 115.0),    // Singapore / Malacca Strait
        new(30.0, -82.0, 42.0, -68.0),   // US East Coast
        new(28.0, 29.0, 32.0, 35.0),     // Suez Canal area
        new(20.0, 110.0, 40.0, 130.0),   // East Asia (China, Korea, Japan)
    ];

    /// <summary>Quick-navigate region presets.</summary>
    public static BoundingBox Europe => new(35.0, -12.0, 65.0, 35.0);
    public static BoundingBox Asia => new(-10.0, 55.0, 45.0, 145.0);
    public static BoundingBox Americas => new(10.0, -100.0, 50.0, -55.0);
    public static BoundingBox Global => new(-60.0, -180.0, 70.0, 180.0);

    public double[][] ToAisStreamFormat() =>
    [
        [MinLatitude, MinLongitude],
        [MaxLatitude, MaxLongitude]
    ];
}
