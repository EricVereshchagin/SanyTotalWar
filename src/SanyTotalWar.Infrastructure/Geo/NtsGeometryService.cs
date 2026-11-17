namespace SanyTotalWar.Infrastructure.Geo;

using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using SanyTotalWar.Application.Abstractions;
using SanyTotalWar.Application.Models;
using SanyTotalWar.Domain.Common;

public sealed class NtsGeometryService : IGeometryService
{
    private const double EarthR = 6371000.0;
    private const double MetersTolerance = 6.0;

    private sealed class Proj
    {
        private readonly double _cosRef;
        public Proj(double refLatDeg) => _cosRef = Math.Cos(refLatDeg * Math.PI / 180.0);
        public double X(double lng) => lng * Math.PI / 180.0 * EarthR * _cosRef;
        public double Y(double lat) => lat * Math.PI / 180.0 * EarthR;
        public GeoPoint Back(double x, double y) => new(
            y / EarthR * 180.0 / Math.PI,
            x / (EarthR * _cosRef) * 180.0 / Math.PI);
    }

    private static Proj ProjOf(GeoPoint? anchor, params IEnumerable<GeoPoint>[] rings)
    {
        foreach (var ring in rings)
        {
            foreach (var p in ring) return new Proj(p.Lat);
        }
        return new Proj(55.7208);
    }

    private static double FirstLat(GeoShape s)
    {
        if (s.Exterior.Count > 0) return s.Exterior[0].Lat;
        foreach (var h in s.Holes)
            if (h.Count > 0)
                return h[0].Lat;
        return 55.7208;
    }

    private static Proj ProjOfShapes(IEnumerable<GeoShape> shapes)
    {
        foreach (var s in shapes)
            return new Proj(FirstLat(s));
        return new Proj(55.7208);
    }

    public bool TryBuildShape(IReadOnlyList<GeoPoint> ring, double minAreaM2, out GeoShape? shape, out string error)
    {
        shape = null;
        error = string.Empty;
        if (ring == null || ring.Count < 4)
        {
            error = "слишком мало точек петли";
            return false;
        }

        var proj = ProjOf(null, ring);
        var coords = ring.Select(p => new Coordinate(proj.X(p.Lng), proj.Y(p.Lat))).ToArray();
        Geometry geometry = new Polygon(new LinearRing(CloseCoords(coords)));
        if (!geometry.IsValid) geometry = geometry.Buffer(0);
        if (geometry.IsEmpty)
        {
            error = "петля самопересекается";
            return false;
        }

        var half = Polygons(geometry).OrderByDescending(p => p.Area).First();
        if (half.Area < minAreaM2)
        {
            error = $"слишком мала петля ({half.Area:0} м², нужно ≥ {minAreaM2:0} м²) — обведите участок пошире";
            return false;
        }

        shape = new GeoShape(RingPoints(half.ExteriorRing, proj), HolesOf(half, proj));
        return true;
    }

    /// <summary>Петля из самопересечения маршрута: Polygonizer выделяет
    /// замкнутую область Netherlands между точками пересечения следа.</summary>
    public bool TryExtractLoop(IReadOnlyList<GeoPoint> track, out GeoShape? loop)
    {
        loop = null;
        if (track is null || track.Count < 4) return false;

        var proj = ProjOf(null, track);
        var coords = track.Select(p => new Coordinate(proj.X(p.Lng), proj.Y(p.Lat))).ToArray();
        Geometry line;
        try
        {
            line = new LineString(coords).Union(new LineString(coords)); // самонодинг
        }
        catch (Exception)
        {
            return false;
        }

        var polygonizer = new NetTopologySuite.Operation.Polygonize.Polygonizer();
        var linework = line is GeometryCollection gc
            ? gc.Geometries.OfType<Geometry>().ToArray()
            : new[] { line };
        polygonizer.Add(linework);
        var rings = polygonizer.GetPolygons();
        if (rings.Count == 0) return false;

        Polygon? best = null;
        foreach (Polygon p in rings)
        {
            if (p.IsEmpty) continue;
            if (best is null || p.Area > best.Area) best = p;
        }
        if (best is null || best.Area <= 0) return false;

        loop = new GeoShape(RingPoints(best.ExteriorRing, proj), HolesOf(best, proj));
        return true;
    }

    public double AreaKm2(IEnumerable<GeoShape> shapes)
    {
        var clean = Union(shapes);
        if (clean.Count == 0) return 0;
        var proj = ProjOfShapes(clean);
        double total = 0;
        foreach (var s in clean)
        {
            var g = ToPolygon(s, proj);
            total += Area(g);
        }
        return total / 1e6;
    }

    public List<GeoShape> Union(IEnumerable<GeoShape> shapes)
    {
        var list = shapes.Where(s => s.Exterior.Count >= 3).ToList();
        if (list.Count == 0) return new List<GeoShape>();
        var proj = ProjOfShapes(list);
        Geometry? acc = null;
        foreach (var s in list)
        {
            var g = ToPolygon(s, proj);
            acc = acc is null ? g : acc.Union(g);
        }
        return Clean(acc!, proj);
    }

    public List<GeoShape> Difference(IEnumerable<GeoShape> source, IEnumerable<GeoShape> subtracted)
    {
        var src = Union(source);
        if (src.Count == 0) return new List<GeoShape>();
        var sub = Union(subtracted);
        if (sub.Count == 0) return src;
        var proj = ProjOfShapes(src.Concat(sub));
        var a = ToMulti(src, proj);
        var b = ToMulti(sub, proj);
        return Clean(a.Difference(b), proj);
    }

    public List<GeoShape> Intersection(GeoShape source, IEnumerable<GeoShape> others)
    {
        var other = Union(others);
        if (other.Count == 0) return new List<GeoShape>();
        var proj = ProjOfShapes(other.Append(source));
        var a = ToPolygon(source, proj);
        var b = ToMulti(other, proj);
        return Clean(a.Intersection(b), proj);
    }

    public bool OverlapOrTouch(IEnumerable<GeoShape> a, IEnumerable<GeoShape> b)
    {
        var la = a.Where(s => s.Exterior.Count >= 3).ToList();
        var lb = b.Where(s => s.Exterior.Count >= 3).ToList();
        if (la.Count == 0 || lb.Count == 0) return false;
        var proj = ProjOfShapes(la.Concat(lb));
        var ga = ToMulti(la, proj);
        var gb = ToMulti(lb, proj);
        return ga.Intersects(gb);
    }

    public double DistanceMeters(IEnumerable<GeoShape> a, IEnumerable<GeoShape> b)
    {
        var la = a.Where(s => s.Exterior.Count >= 3).ToList();
        var lb = b.Where(s => s.Exterior.Count >= 3).ToList();
        if (la.Count == 0 || lb.Count == 0) return double.MaxValue;
        var proj = ProjOfShapes(la.Concat(lb));
        var ga = ToMulti(la, proj);
        var gb = ToMulti(lb, proj);
        return ga.Distance(gb);
    }

    public bool CoversPoint(IEnumerable<GeoShape> shapes, GeoPoint point)
    {
        var list = shapes.Where(s => s.Exterior.Count >= 3).ToList();
        if (list.Count == 0) return false;
        var proj = ProjOfShapes(list);
        var pt = new Point(proj.X(point.Lng), proj.Y(point.Lat));
        foreach (var s in list)
        {
            var g = ToPolygon(s, proj);
            if (!g.IsEmpty && g.Covers(pt)) return true;
        }
        return false;
    }

    public GeoPoint Centroid(IEnumerable<GeoShape> shapes)
    {
        var list = shapes.Where(s => s.Exterior.Count >= 3).ToList();
        if (list.Count == 0) return new GeoPoint(0, 0);
        var proj = ProjOfShapes(list);
        var c = ToMulti(list, proj).Centroid;
        return proj.Back(c.X, c.Y);
    }

    public List<GeoPoint> PerimeterSample(IEnumerable<GeoShape> shapes, int count, Random rng)
    {
        var result = new List<GeoPoint>();
        var list = shapes.Where(s => s.Exterior.Count >= 3).ToList();
        if (list.Count == 0 || count <= 0) return result;
        var proj = ProjOfShapes(list);
        var polys = list.Select(s => ToPolygon(s, proj)).Where(p => !p.IsEmpty).ToList();
        if (polys.Count == 0) return result;
        var perPoly = (int)Math.Ceiling((double)count / polys.Count);
        foreach (var poly in polys)
            result.AddRange(SampleRing(poly.ExteriorRing, perPoly, rng, proj));
        return result.Take(count).ToList();
    }

    public List<List<GeoShape>> ConnectedComponents(IEnumerable<GeoShape> shapes)
    {
        var items = shapes.Where(s => s.Exterior.Count >= 3).ToList();
        var groups = new List<List<GeoShape>>();
        foreach (var s in items)
        {
            var merged = new List<GeoShape>();
            foreach (var group in groups.ToList())
            {
                var proj = ProjOfShapes(group.Append(s));
                var ga = ToMulti(group, proj);
                var gb = ToPolygon(s, proj);
                if (ga.Intersects(gb))
                {
                    merged.AddRange(group);
                    groups.Remove(group);
                }
            }
            merged.Add(s);
            groups.Add(merged);
        }
        return groups;
    }

    // ------------------------------------------------------------ internal

    private static double Area(Geometry g) =>
        Polygons(g).Sum(p => p.Area);

        private static Polygon ToPolygon(GeoShape s, Proj proj)
    {
        var shell = new LinearRing(CloseCoords(s.Exterior.Select(p => new Coordinate(proj.X(p.Lng), proj.Y(p.Lat))).ToArray()));
        var holes = s.Holes
            .Where(h => h.Count >= 3)
            .Select(h => new LinearRing(CloseCoords(h.Select(p => new Coordinate(proj.X(p.Lng), proj.Y(p.Lat))).ToArray())))
            .ToArray();
        return new Polygon(shell, holes);
    }

    private static Geometry ToMulti(IEnumerable<GeoShape> shapes, Proj proj)
    {
        var polys = shapes.Select(s => ToPolygon(s, proj)).ToArray();
        return polys.Length == 1 ? polys[0] : new MultiPolygon(polys);
    }

    private static Coordinate[] CloseCoords(Coordinate[] coords)
    {
        var list = coords.ToList();
        if (list.Count > 0)
        {
            var first = list[0];
            var last = list[^1];
            if (Math.Abs(first.X - last.X) > 1e-9 || Math.Abs(first.Y - last.Y) > 1e-9)
                list.Add(new Coordinate(first.X, first.Y));
        }
        return list.ToArray();
    }

    private List<GeoShape> Clean(Geometry geometry, Proj proj)
    {
        var result = new List<GeoShape>();
        if (geometry is null || geometry.IsEmpty) return result;
        var simplified = DouglasPeuckerSimplifier.Simplify(geometry, MetersTolerance);
        if (simplified is null || simplified.IsEmpty) return result;
        if (!simplified.IsValid) simplified = simplified.Buffer(0);
        if (simplified.IsEmpty) return result;
        foreach (var poly in Polygons(simplified))
        {
            if (poly.IsEmpty || poly.Area < 40) continue;
            result.Add(new GeoShape(RingPoints(poly.ExteriorRing, proj), HolesOf(poly, proj)));
        }
        return result;
    }

    private static IEnumerable<Polygon> Polygons(Geometry geometry)
    {
        if (geometry is Polygon p) return new[] { p };
        if (geometry is GeometryCollection gc)
            return gc.Geometries.OfType<Polygon>();
        return Enumerable.Empty<Polygon>();
    }

    private static List<List<GeoPoint>> HolesOf(Polygon poly, Proj proj)
    {
        var holes = new List<List<GeoPoint>>();
        for (var i = 0; i < poly.NumInteriorRings; i++)
            holes.Add(RingPoints(poly.GetInteriorRingN(i), proj));
        return holes;
    }

    private static List<GeoPoint> RingPoints(LineString ring, Proj proj)
    {
        var pts = new List<GeoPoint>(ring.Coordinates.Length);
        var coords = ring.Coordinates;
        for (var i = 0; i < coords.Length; i++)
        {
            var isLastDup = i == coords.Length - 1 &&
                            Math.Abs(coords[i].X - coords[0].X) < 1e-9 &&
                            Math.Abs(coords[i].Y - coords[0].Y) < 1e-9;
            if (isLastDup && coords.Length > 3) continue;
            pts.Add(proj.Back(coords[i].X, coords[i].Y));
        }
        return pts;
    }

    private static List<GeoPoint> SampleRing(LineString ring, int count, Random rng, Proj proj)
    {
        var result = new List<GeoPoint>();
        var coords = ring.Coordinates;
        if (coords.Length < 2 || count <= 0) return result;
        var segLen = new double[coords.Length - 1];
        double total = 0;
        for (var i = 0; i < segLen.Length; i++)
        {
            var dx = coords[i + 1].X - coords[i].X;
            var dy = coords[i + 1].Y - coords[i].Y;
            segLen[i] = Math.Sqrt(dx * dx + dy * dy);
            total += segLen[i];
        }
        if (total < 1e-6) return result;
        for (var k = 0; k < count; k++)
        {
            var target = total * ((k + 0.5) / count);
            var acc = 0.0;
            for (var i = 0; i < segLen.Length; i++)
            {
                if (acc + segLen[i] < target && i < segLen.Length - 1)
                {
                    acc += segLen[i];
                    continue;
                }
                var t = segLen[i] > 0 ? (target - acc) / segLen[i] : 0;
                t = Math.Max(0, Math.Min(1, t));
                var x = coords[i].X + (coords[i + 1].X - coords[i].X) * t;
                var y = coords[i].Y + (coords[i + 1].Y - coords[i].Y) * t;
                x += (rng.NextDouble() * 2 - 1) * 15.0;
                y += (rng.NextDouble() * 2 - 1) * 15.0;
                result.Add(proj.Back(x, y));
                break;
            }
        }
        return result;
    }
}
