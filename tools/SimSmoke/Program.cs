using SanyTotalWar.Application;
using SanyTotalWar.Application.Abstractions;
using SanyTotalWar.Application.Models;
using SanyTotalWar.Domain;
using SanyTotalWar.Domain.Common;
using SanyTotalWar.Domain.Entities;
using SanyTotalWar.Infrastructure.Geo;
using SanyTotalWar.Infrastructure.Persistence;

IWorldRepository repo = new InMemoryWorldRepository();
IGeometryService geo = new NtsGeometryService();
var engine = new GameEngine(repo, geo);
var world = repo.World;
var meId = world.Players.First(p => !p.IsBot).Id;
var rng = new Random(7);

void Dump(string title)
{
    Console.WriteLine($"--- {title} ---");
    Console.WriteLine($"t={world.GameNow:MM.dd HH:mm}");
    foreach (var t in world.Territories)
        Console.WriteLine($"  [{world.PlayerById(t.OwnerId)!.Name,-16}] {t.Name,-22} area={engine.AreaKm2(t):0.000} km2 army={t.Army}/{engine.ArmyLimitOf(t)} bld={string.Join(',', t.Buildings.Select(b => b.Type))}");
    foreach (var w in world.Wars)
        Console.WriteLine($"  WAR {w.Name}{(w.Ended ? " (ENDED: " + w.EndReason + ")" : "")} zone={w.ZoneAreaKm2:0.000} km2 sides=[{string.Join("; ", w.Sides.Select(s => $"{world.PlayerById(s.PlayerId)!.Name}:{s.Mobilized}"))}] pts={w.Points.Count(x => x.CapturedBy != null)}/{w.Points.Count}");
}

static GeoPoint Shift(GeoPoint p, double dx, double dy) => GeoSketch.OffsetM(p, dx, dy);

static double MaxDeviation(List<GeoPoint> drawn, List<GeoPoint> stored)
{
    var max = 0.0;
    foreach (var a in drawn)
    {
        var best = double.MaxValue;
        foreach (var b in stored)
            best = Math.Min(best, GeoMath.ApproxDistanceM(a, b));
        if (best < double.MaxValue) max = Math.Max(max, best);
    }
    return max;
}

List<GeoPoint> ClosedRing(GeoPoint center, double r, int segments, Random r0)
{
    var ring = GeoSketch.Ring(center, r, segments, 0.04, r0);
    ring.Add(ring[0]);
    return ring;
}

List<GeoPoint> RingAt(double dx, double dy, double r, int seed)
{
    var rr = new Random(seed);
    var ring = GeoSketch.Ring(GeoSketch.OffsetM(WorldSeeder.Center, dx, dy), r, 14, 0.03, rr);
    ring.Add(ring[0]);
    return ring;
}

void Report(Guid who, List<EngineEvent> events)
{
    var name = world.PlayerById(who)!.Name;
    foreach (var e in events) Console.WriteLine($"   [{name}] {e.Kind}: {e.Message}");
}

Dump("SEED");

// UC-01: захват нейтрального участка + проверка точности контура
Console.WriteLine("\n== UC-01 capture neutral + fidelity ==");
var loop1 = RingAt(1500, 300, 260, 11);
for (var i = 0; i < loop1.Count; i += 3) loop1[i] = GeoSketch.OffsetM(loop1[i], rng.NextDouble() * 20 - 10, rng.NextDouble() * 20 - 10);
loop1.Add(loop1[0]);
Report(meId, engine.AddTrack(meId, loop1));
var cap = world.TerritoriesOf(meId).First(t => t.Name == "Сектор 1");
Console.WriteLine($"   max deviation drawn vs stored: {MaxDeviation(loop1, cap.Parts.SelectMany(p => p.Exterior).ToList()):0.0} m");
Console.WriteLine($"   parts={cap.Parts.Count} vertices={cap.Parts.Sum(p => p.Exterior.Count)} (drawn {loop1.Count})");

// Ходьба кругом внутри своей территории: контур не должен измениться
Console.WriteLine("\n== walk circle inside own land ==");
var beforeParts = cap.Parts.SelectMany(p => p.Exterior).ToList();
var beforeArea = engine.AreaKm2(cap);
var innerWalk = geo.Centroid(engine.PartsAsShapes(cap));
Report(meId, engine.AddTrack(meId, ClosedRing(innerWalk, 120, 12, rng)));
DumpMeters(MaxDeviation(beforeParts, cap.Parts.SelectMany(p => p.Exterior).ToList()), beforeArea, engine.AreaKm2(cap), cap.Parts.Count);

// Захват дугой: открытый маршрут от границы своей земли в нейтраль и обратно к границе
Console.WriteLine("\n== arc capture (no loop) ==");
var arc = new List<GeoPoint>
{
    GeoSketch.OffsetM(WorldSeeder.Center, 150, 280),    // старт у северной границы Плацдарма
    GeoSketch.OffsetM(WorldSeeder.Center, 90, 480),
    GeoSketch.OffsetM(WorldSeeder.Center, 0, 560),      // вершина дуги в нейтральной земле
    GeoSketch.OffsetM(WorldSeeder.Center, -90, 480),
    GeoSketch.OffsetM(WorldSeeder.Center, -150, 280),   // конец у границы
    GeoSketch.OffsetM(WorldSeeder.Center, -60, 240),
};
var mergedBefore = engine.AreaKm2(world.TerritoriesOf(meId).OrderByDescending(engine.AreaKm2).First());
Report(meId, engine.AddTrack(meId, arc));
var mergedAfter = engine.AreaKm2(world.TerritoriesOf(meId).OrderByDescending(engine.AreaKm2).First());
Console.WriteLine($"   largest own territory: {mergedBefore:0.000} -> {mergedAfter:0.000} km2 (дуга должна быть влита)");

// Автослияние смежных независимых земель (UC-04)
Console.WriteLine("\n== coalesce adjacent own territories ==");
var loopA = RingAt(2400, 900, 200, 31); loopA.Add(loopA[0]);
Report(meId, engine.AddTrack(meId, loopA));
var loopB = RingAt(2400 + 380, 900, 200, 32); loopB.Add(loopB[0]); // почти касаются: зазор ~ -10..20 м
Report(meId, engine.AddTrack(meId, loopB));
var sectorsNear = world.TerritoriesOf(meId).Where(t => engine.AreaKm2(t) > 0.05 && geo.DistanceMeters(new[] { GeoShape.Simple(new List<GeoPoint> { new GeoPoint(WorldSeeder.Center.Lat + 0.009, WorldSeeder.Center.Lng + 0.021) }) }, engine.PartsAsShapes(t)) < 1500).ToList();
Console.WriteLine($"   territories vivre near (2400,900): {sectorsNear.Count} (1 = слиты) names=[{string.Join(", ", sectorsNear.Select(s => s.Name))}] areas=[{string.Join(", ", sectorsNear.Select(s => engine.AreaKm2(s).ToString("0.00")))}]");

static void DumpMeters(double dev, double a0, double a1, int parts) =>
    Console.WriteLine($"   deviation: {dev:0.0} m; area {a0:0.000} -> {a1:0.000} km2; parts={parts}");
Dump("after capture");

// UC-04: слияние прилегающей петли (перекрывает край своей земли)
Console.WriteLine("\n== UC-04 merge adjacent ==");
Report(meId, engine.AddTrack(meId, RingAt(0, 470, 240, 12)));
Dump("after merge");

// UC-02/03: пополнение армии и лимит
Console.WriteLine("\n== UC-02/03 army gain + cap ==");
var home = world.TerritoriesOf(meId).OrderByDescending(t => t.Army).First();
Console.WriteLine($"home={home.Name} army={home.Army}/{engine.ArmyLimitOf(home)}");
for (var i = 0; i < 4; i++)
{
    var before = home.Army;
    var path = GeoSketch.Path(GeoSketch.OffsetM(WorldSeeder.Center, 1500, 520), 400, 3, 0.7 * i);
    path.Reverse();
    Report(meId, engine.AddTrack(meId, path));
    Console.WriteLine($"   march #{i}: {before} -> {home.Army} (limit {engine.ArmyLimitOf(home)})");
}

// UC-13: постройка военкомата
Console.WriteLine("\n== UC-13 build ==");
home.Army = Math.Max(home.Army, 400);
Report(meId, engine.Build(meId, home.Id, BuildingType.ConscriptionOffice));
Report(meId, engine.Build(meId, home.Id, BuildingType.ConscriptionOffice)); // лимит 1

// UC-06: атака территории бота (петля поверх края "Земля Красная Орда")
Console.WriteLine("\n== UC-06 attack enemy ==");
var reds = world.Players.First(p => p.Name == "Красная Орда");
var redsTerr = world.TerritoriesOf(reds.Id).First();
Console.WriteLine($"reds territory area={engine.AreaKm2(redsTerr):0.000} army={redsTerr.Army}");
var redsCenter = geo.Centroid(engine.PartsAsShapes(redsTerr));
var edge1 = GeoSketch.OffsetM(redsCenter, -180, 0);
Report(meId, engine.AddTrack(meId, ClosedRing(edge1, 150, 12, rng)));
Dump("war started");

// UC-10.5: игрок берёт точки — идём через зону войны
Console.WriteLine("\n== UC-10.5 points ==");
var war = world.ActiveWars.First();
foreach (var pt in war.Points.Where(p => p.CapturedBy == null).ToList())
{
    var walk = new List<GeoPoint> { GeoSketch.OffsetM(pt.Position, -30, 0), GeoSketch.OffsetM(pt.Position, 40, -5) };
    Report(meId, engine.AddTrack(meId, walk));
}
Console.WriteLine($"   captured by me: {war.Points.Count(p => p.CapturedBy == meId)}/{war.Points.Count}");

// UC-07: третий игрок вступает в войну петлёй поверх зоны
Console.WriteLine("\n== UC-07 third side joins ==");
var steel = world.Players.First(p => p.Name == "Стальной Легион");
var zoneShapes = war.ZoneParts.Select(z => new GeoShape(z.Exterior, z.Holes)).ToList();
var zoneCenter = geo.Centroid(zoneShapes);
Report(steel.Id, engine.AddTrack(steel.Id, ClosedRing(zoneCenter, 90, 10, rng)));

var warObj = world.WarById(war.Id)!;
Dump("after join");

// UC-08: подкрепление из смежной территории атакующего
Console.WriteLine("\n== UC-08 reinforcements ==");
var opts = engine.ReinforceOptions(meId, warObj.Id);
foreach (var o in opts) Console.WriteLine($"   opt: {o.Name} army={o.Army} dist={o.DistanceM:0} adjacent={o.Adjacent} airport={o.HasAirport}");
var best = opts.First(o => o.Adjacent || o.HasAirport);
Report(meId, engine.SendReinforcements(meId, warObj.Id, best.TerritoryId, Math.Min(30, best.Army)));

// UC-09: отступление с потерей 50%
Console.WriteLine("\n== UC-09 retreat ==");
Report(meId, engine.Retreat(meId, warObj.Id, Math.Max(1, warObj.SideOf(meId)!.Mobilized / 2)));

// Прокрутка времени: бой UC-10, волны точек, завершение UC-11/12, UC-05
Console.WriteLine("\n== time flow x17 ==");
engine.Tick(TimeSpan.FromHours(17));
Dump("after 17 h");

engine.Tick(TimeSpan.FromHours(9));
Dump("after 26 h");

// UC-14: пассивное уменьшение
Console.WriteLine("\n== UC-14 decay: наблюдаемая армия до/после 25 ч ==");
var t0 = world.Territories.Where(t => t.Army > 0).OrderByDescending(t => t.Army).First();
var a0 = t0.Army;
engine.Tick(TimeSpan.FromHours(25));
Console.WriteLine($"   {t0.Name}: {a0} -> {t0.Army} (limit {engine.ArmyLimitOf(t0)})");

// UC-05: принудительно устроим войну в середине крупной территории
Console.WriteLine("\n== UC-05 split after interior war ==");
var steelTerr = world.TerritoriesOf(steel.Id).First();
var steelArea = engine.AreaKm2(steelTerr);
var innerSteel = geo.Centroid(engine.PartsAsShapes(steelTerr));
Console.WriteLine($"steel territory: {steelArea:0.000} km2, army {steelTerr.Army}");
Report(meId, engine.AddTrack(meId, ClosedRing(innerSteel, 100, 12, rng)));
var war2 = world.ActiveWars.FirstOrDefault(w => w.ContextOwnerAtStart == steel.Id);
if (war2 == null) Console.WriteLine("   (война не началась)");
else
{
    war2.Sides.First(s => s.PlayerId == steel.Id).Mobilized = 10;
    engine.Tick(TimeSpan.FromHours(30));
    Dump("after UC-05 split");
}

Console.WriteLine("\nALL CHECKS DONE");
