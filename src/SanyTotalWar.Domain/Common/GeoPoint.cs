namespace SanyTotalWar.Domain.Common;

public readonly record struct GeoPoint(double Lat, double Lng)
{
    public static GeoPoint FromXYMeters(double xMeters, double yMeters, double refLatDeg)
    {
        const double r = 6371000.0;
        var refLatRad = refLatDeg * Math.PI / 180.0;
        var lng = xMeters / (r * Math.Cos(refLatRad)) * 180.0 / Math.PI;
        var lat = yMeters / r * 180.0 / Math.PI;
        return new GeoPoint(lat, lng);
    }
}
