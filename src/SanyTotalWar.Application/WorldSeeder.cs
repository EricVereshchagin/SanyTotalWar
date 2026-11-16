namespace SanyTotalWar.Application;

using SanyTotalWar.Application.Abstractions;
using SanyTotalWar.Application.Models;
using SanyTotalWar.Domain;
using SanyTotalWar.Domain.Common;
using SanyTotalWar.Domain.Entities;
using SanyTotalWar.Domain.Policies;

public static class WorldSeeder
{
    public static readonly GeoPoint Center = new(55.7208, 37.6018);

    public static void Seed(World world)
    {
        world.GameStart = DateTime.Now;
        world.GameNow = world.GameStart;

        var me = new Player { Name = "Ваше командование", IsBot = false, ColorHex = "#e8b23d" };
        var reds = new Player { Name = "Красная Орда", IsBot = true, ColorHex = "#d8483b" };
        var steel = new Player { Name = "Стальной Легион", IsBot = true, ColorHex = "#4f7cc0" };
        var free = new Player { Name = "Вольный Корпус", IsBot = true, ColorHex = "#9a6dc9" };
        world.Players.AddRange(new[] { me, reds, steel, free });

        AddTerritory(world, me, Ring(Center, 230, 14, 0.10), armyShare: 0.45, buildings: BuildingType.ConscriptionOffice);
        AddTerritory(world, reds, Ring(Offset(Center, 640, -420), 240, 14, 0.16), armyShare: 0.55, buildings: BuildingType.Fortress);
        AddTerritory(world, steel, Ring(Offset(Center, -560, 560), 260, 14, 0.16), armyShare: 0.6, buildings: BuildingType.ConscriptionOffice);
        AddTerritory(world, free, Ring(Offset(Center, -900, -350), 200, 14, 0.2), armyShare: 0.45);

        world.Log("system", "Оперативная обстановка развёрнута. Захватывайте земли петлями маршрутов, наращивайте армию и следите за соседями");
    }

    private static Territory AddTerritory(
        World world, Player owner, List<GeoPoint> ring,
        double armyShare = 0.6, BuildingType? buildings = null)
    {
        var t = new Territory
        {
            OwnerId = owner.Id,
            Name = owner.IsBot ? $"Земля {owner.Name}" : "Плацдарм «Командование»",
            Parts = new List<TerritoryPart> { new() { Exterior = ring } },
            CreatedAtGameTime = world.GameNow,
        };
        t.Army = (int)(ArmyPolicy.ArmyLimitFor(TotalAreaKm2(world, t)) * armyShare);
        if (buildings is { } b) t.Buildings.Add(new Building { Type = b, BuiltAtGameTime = world.GameNow });
        world.Territories.Add(t);
        return t;
    }

    private static double TotalAreaKm2(World world, Territory t)
    {
        var shape = t.Parts[0].Exterior;
        var area = 0.0;
        for (var i = 0; i < shape.Count; i++)
        {
            var a = shape[i];
            var b = shape[(i + 1) % shape.Count];
            area += (a.Lng * Math.PI / 180) * (b.Lat * Math.PI / 180) - (b.Lng * Math.PI / 180) * (a.Lat * Math.PI / 180);
        }
        var refLat = Center.Lat;
        return Math.Abs(area) * Math.Pow(6371000.0 * Math.Cos(refLat * Math.PI / 180.0), 2) / 2.0 / 1e6;
    }

    private static GeoPoint Offset(GeoPoint p, double dx, double dy) => GeoSketch.OffsetM(p, dx, dy);

    private static List<GeoPoint> Ring(GeoPoint c, double r, int n, double jitter) => GeoSketch.Ring(c, r, n, jitter, new Random(c.Lng.GetHashCode()));
}
