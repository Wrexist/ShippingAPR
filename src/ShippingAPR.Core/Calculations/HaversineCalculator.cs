namespace ShippingAPR.Core.Calculations;

public static class HaversineCalculator
{
    private const double EarthRadiusNauticalMiles = 3440.065;

    public static double DistanceInNauticalMiles(
        double lat1, double lon1,
        double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var lat1Rad = DegreesToRadians(lat1);
        var lat2Rad = DegreesToRadians(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusNauticalMiles * c;
    }

    public static double DistanceInKilometers(
        double lat1, double lon1,
        double lat2, double lon2) =>
        DistanceInNauticalMiles(lat1, lon1, lat2, lon2) * 1.852;

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180.0;
}
