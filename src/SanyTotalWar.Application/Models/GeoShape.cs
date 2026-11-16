namespace SanyTotalWar.Application.Models;

using SanyTotalWar.Domain.Common;

public sealed record GeoShape(List<GeoPoint> Exterior, List<List<GeoPoint>> Holes)
{
    public static GeoShape Simple(IEnumerable<GeoPoint> ring) => new(ring.ToList(), new List<List<GeoPoint>>());
}
