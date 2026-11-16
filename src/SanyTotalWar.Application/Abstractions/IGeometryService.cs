namespace SanyTotalWar.Application.Abstractions;

using SanyTotalWar.Application.Models;
using SanyTotalWar.Domain.Common;

public interface IGeometryService
{
    bool TryBuildShape(IReadOnlyList<GeoPoint> ring, double minAreaM2, out GeoShape? shape, out string error);

    double AreaKm2(IEnumerable<GeoShape> shapes);

    List<GeoShape> Union(IEnumerable<GeoShape> shapes);

    List<GeoShape> Difference(IEnumerable<GeoShape> source, IEnumerable<GeoShape> subtracted);

    List<GeoShape> Intersection(GeoShape source, IEnumerable<GeoShape> others);

    bool OverlapOrTouch(IEnumerable<GeoShape> a, IEnumerable<GeoShape> b);

    double DistanceMeters(IEnumerable<GeoShape> a, IEnumerable<GeoShape> b);

    bool CoversPoint(IEnumerable<GeoShape> shapes, GeoPoint point);

    GeoPoint Centroid(IEnumerable<GeoShape> shapes);

    List<GeoPoint> PerimeterSample(IEnumerable<GeoShape> shapes, int count, Random rng);

    List<List<GeoShape>> ConnectedComponents(IEnumerable<GeoShape> shapes);
}
