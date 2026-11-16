namespace SanyTotalWar.Application;

using SanyTotalWar.Application.Models;
using SanyTotalWar.Domain;
using SanyTotalWar.Domain.Common;
using SanyTotalWar.Domain.Entities;

public sealed class BotDirector
{
    public void Hourly(World world, GameEngine engine, Random rng)
    {
        foreach (var bot in world.Players.Where(p => p.IsBot).ToList())
        {
            if (rng.NextDouble() < 0.25) WalkForArmy(engine, bot, rng);
            if (rng.NextDouble() < 0.18) TryExpand(engine, world, bot, rng);
            if (rng.NextDouble() < 0.02) TryAttack(engine, bot, rng);

            foreach (var war in world.ActiveWars.Where(w => w.IsParticipant(bot.Id)).ToList())
                Decide(engine, war, bot, rng);
        }
    }

    private void WalkForArmy(GameEngine engine, Player bot, Random rng)
    {
        var home = engine.W.TerritoriesOf(bot.Id).OrderByDescending(_ => rng.Next()).FirstOrDefault();
        if (home is null || home.Parts.Count == 0) return;
        var start = home.Parts[0].Exterior.FirstOrDefault();
        if (start == default) return;
        var dir = rng.NextDouble() * 2 * Math.PI;
        var pts = GeoSketch.Path(start, 60 + rng.Next(120), 3, dir);
        pts.Reverse();
        engine.AddTrack(bot.Id, pts);
    }

    private void TryExpand(GameEngine engine, World world, Player bot, Random rng)
    {
        var home = engine.W.TerritoriesOf(bot.Id).OrderByDescending(_ => rng.Next()).FirstOrDefault();
        if (home is null) return;
        var homeRing = home.Parts[rng.Next(home.Parts.Count)].Exterior;
        var vertex = homeRing[rng.Next(homeRing.Count)];
        var center = RoughCentroid(homeRing);
        var dx = (vertex.Lng - center.Lng) * 111320.0 * Math.Cos(center.Lat * Math.PI / 180.0);
        var dy = (vertex.Lat - center.Lat) * 111320.0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var r = 90 + rng.Next(100);
        var anchor = GeoSketch.OffsetM(vertex, dx / len * r * 1.05, dy / len * r * 1.05);
        var ring = GeoSketch.Ring(anchor, r, 10, 0.18, rng);
        ring.Add(ring[0]);
        engine.AddTrack(bot.Id, ring);
    }

    private void TryAttack(GameEngine engine, Player bot, Random rng)
    {
        var mine = engine.W.TerritoriesOf(bot.Id).ToList();
        if (mine.Count == 0) return;
        var enemyTerr = engine.W.Territories
            .Where(t => t.OwnerId != bot.Id)
            .OrderBy(_ => rng.Next())
            .FirstOrDefault();
        if (enemyTerr is null || enemyTerr.Parts.Count == 0) return;
        var enemyRing = enemyTerr.Parts[rng.Next(enemyTerr.Parts.Count)].Exterior;
        var vertex = enemyRing[rng.Next(enemyRing.Count)];
        var center = RoughCentroid(enemyRing);
        var dx = (vertex.Lng - center.Lng) * 111320.0 * Math.Cos(center.Lat * Math.PI / 180.0);
        var dy = (vertex.Lat - center.Lat) * 111320.0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var r = 110 + rng.Next(120);
        var anchor = GeoSketch.OffsetM(vertex, -dx / len * r * 0.3, -dy / len * r * 0.3);
        var loop = GeoSketch.Ring(anchor, r, 10, 0.18, rng);
        loop.Add(loop[0]);
        engine.AddTrack(bot.Id, loop);
    }

    private static GeoPoint RoughCentroid(List<GeoPoint> ring)
    {
        double lat = 0, lng = 0;
        foreach (var p in ring)
        {
            lat += p.Lat;
            lng += p.Lng;
        }
        var n = Math.Max(1, ring.Count);
        return new GeoPoint(lat / n, lng / n);
    }

    private void Decide(GameEngine engine, War war, Player bot, Random rng)
    {
        var mySide = war.SideOf(bot.Id);
        if (mySide is null) return;

        var visits = (int)Math.Ceiling(rng.NextDouble() * war.PointsPerCycle * 0.6);
        engine.BotVisitPoints(war, bot.Id, visits);

        var strongest = war.Sides.Where(s => s.PlayerId != bot.Id).Max(s => s.Mobilized);

        if (mySide.Mobilized <= 0)
        {
            var opt = engine.ReinforceOptions(bot.Id, war.Id).OrderBy(o => o.DistanceM).FirstOrDefault();
            if (opt is { Adjacent: true } && rng.NextDouble() < 0.7)
            {
                var send = Math.Max(1, (int)(opt.Army * 0.35));
                engine.SendReinforcements(bot.Id, war.Id, opt.TerritoryId, send);
            }
            return;
        }

        if (strongest <= 0) return;

        if (mySide.Mobilized * 4 <= strongest && rng.NextDouble() < 0.65)
        {
            engine.Retreat(bot.Id, war.Id, mySide.Mobilized);
            return;
        }

        if (mySide.Mobilized * 2 <= strongest && rng.NextDouble() < 0.75)
        {
            var opt = engine.ReinforceOptions(bot.Id, war.Id)
                .Where(o => o.Army > 20)
                .OrderBy(o => o.DistanceM)
                .FirstOrDefault();
            if (opt is not null)
            {
                var send = Math.Max(1, (int)Math.Min(opt.Army * 0.35, mySide.Mobilized));
                engine.SendReinforcements(bot.Id, war.Id, opt.TerritoryId, send);
            }
        }
    }
}
