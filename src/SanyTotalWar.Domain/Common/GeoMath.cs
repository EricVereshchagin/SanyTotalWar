namespace SanyTotalWar.Domain.Common;

public static class GeoMath
{
    public const double EarthRadiusM = 6371000.0;

    public static double ApproxDistanceM(GeoPoint a, GeoPoint b)
    {
        var dLat = (b.Lat - a.Lat) * Math.PI / 180.0;
        var dLng = (b.Lng - a.Lng) * Math.PI / 180.0;
        var midLat = (a.Lat + b.Lat) * 0.5 * Math.PI / 180.0;
        var x = dLng * Math.Cos(midLat);
        return EarthRadiusM * Math.Sqrt(x * x + dLat * dLat);
    }

    public static double TrackLengthM(IReadOnlyList<GeoPoint> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++) total += ApproxDistanceM(points[i - 1], points[i]);
        return total;
    }
}
