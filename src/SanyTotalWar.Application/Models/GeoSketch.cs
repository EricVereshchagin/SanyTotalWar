namespace SanyTotalWar.Application.Models;

using SanyTotalWar.Domain.Common;

public static class GeoSketch
{
    public static GeoPoint OffsetM(GeoPoint p, double dxMeters, double dyMeters)
    {
        var lat = p.Lat + dyMeters / 111320.0;
        var lng = p.Lng + dxMeters / (111320.0 * Math.Cos(p.Lat * Math.PI / 180.0));
        return new GeoPoint(lat, lng);
    }

    public static List<GeoPoint> Ring(GeoPoint center, double radiusM, int segments, double jitter, Random rng)
    {
        var pts = new List<GeoPoint>(segments);
        for (var i = 0; i < segments; i++)
        {
            var angle = 2.0 * Math.PI * i / segments;
            var r = radiusM * (1.0 + (rng.NextDouble() * 2 - 1) * jitter);
            pts.Add(OffsetM(center, Math.Cos(angle) * r, Math.Sin(angle) * r * 1.15));
        }
        return pts;
    }

    public static List<GeoPoint> Path(GeoPoint start, double stepM, int steps, double dirRad)
    {
        var pts = new List<GeoPoint>();
        for (var i = 0; i < steps; i++)
        {
            var d = stepM * i;
            pts.Add(OffsetM(start, Math.Cos(dirRad) * d, Math.Sin(dirRad) * d));
        }
        return pts;
    }
}
