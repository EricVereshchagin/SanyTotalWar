namespace SanyTotalWar.Application;

using SanyTotalWar.Application.Abstractions;
using SanyTotalWar.Application.Models;
using SanyTotalWar.Domain;
using SanyTotalWar.Domain.Common;
using SanyTotalWar.Domain.Entities;
using SanyTotalWar.Domain.Policies;

public sealed record EngineEvent(string Kind, string Message);

public sealed record ReinforceOption(Guid TerritoryId, string Name, int Army, double DistanceM, bool Adjacent, bool HasAirport);

public sealed class GameEngine
{
    public const double MinLoopAreaM2 = 900;
    public const double MinZoneAreaM2 = 350;
    public const double MinPieceAreaM2 = 250;
    public const double LoopClosureM = 90;
    public const double PointCaptureRadiusM = 80;
    public const double FortressRadiusM = 500;
    public const double MergeToleranceM = 60;
    public const double MinSplitPieceM2 = 1500;

    private readonly IWorldRepository _repo;
    private readonly IGeometryService _geo;
    private readonly BotDirector _bots = new();
    private readonly Random _rng = new();
    private double _hourAccumulator;

    public GameEngine(IWorldRepository repo, IGeometryService geo)
    {
        _repo = repo;
        _geo = geo;
    }

    public World W => _repo.World;

    // --------------------------------------------------------------- UC-01/02/03/04/06/07: маршрут и захват

    public List<EngineEvent> AddTrack(Guid playerId, List<GeoPoint> points)
    {
        var events = new List<EngineEvent>();
        var world = W;
        lock (world.Gate)
        {
            var player = world.PlayerById(playerId);
            if (player is null || points is null || points.Count < 2)
            {
                world.Bump();
                return events;
            }

            var meters = GeoMath.TrackLengthM(points);

            var closed = points.Count >= 6 &&
                         GeoMath.ApproxDistanceM(points[0], points[^1]) <= LoopClosureM;

            if (player.IsBot && meters > 80)
            {
                world.RecentTracks.RemoveAll(t => world.GameNow - t.AtGameTime > TimeSpan.FromHours(3));
                world.RecentTracks.Add(new TrackMark
                {
                    PlayerId = playerId,
                    Points = points.ToList(),
                    AtGameTime = world.GameNow,
                    Loop = closed,
                });
                if (world.RecentTracks.Count > 30)
                    world.RecentTracks.RemoveRange(0, world.RecentTracks.Count - 30);
            }

            var home = OwnTerritoryCovering(playerId, points[^1]) ?? LargestOwn(playerId);
        if (home is null)
        {
            events.Add(new EngineEvent("warn", "У вас нет территории — войска за марш не начисляются"));
        }
        else
        {
            var limit = ArmyLimitOf(home);
            var raw = ArmyPolicy.SoldiersForWalk(meters, ArmyPolicy.WalkBonusMul(home));
            var before = home.Army;
            home.Army = Math.Min(limit, home.Army + raw);
            var gained = home.Army - before;
            if (gained > 0)
            {
                events.Add(new EngineEvent("army", $"+{gained} солдат за марш {meters:0} м"));
                world.Log("army", $"{home.Name}: пополнение +{gained} (марш {meters:0} м)");
            }
            if (raw > gained)
            {
                events.Add(new EngineEvent("warn", $"Лимит армии «{home.Name}» ({limit}) достигнут — войска не начисляются (UC-03)"));
                world.Log("system", $"{home.Name} (UC-03): лимит {limit}, войска не начисляются");
            }
        }

        VisitWarPoints(playerId, points, events);

        // UC-01/04: замкнутая петля или дуга от границ своей земли
        GeoShape? captureShape = null;
        if (closed)
        {
            if (!_geo.TryBuildShape(points, MinLoopAreaM2, out var shape, out var error))
            {
                events.Add(new EngineEvent("warn", $"Петля не принята: {error}"));
            }
            else
            {
                captureShape = shape!;
            }
        }
        else if (TryArcCapture(player, points, meters, events, out var arcShape, out var arcError))
        {
            captureShape = arcShape;
        }
        else if (arcError is not null)
        {
            events.Add(arcError);
        }

        if (captureShape is not null) ProcessCapture(player, captureShape, events);
        else if (!closed && meters > 40) events.Add(new EngineEvent("info", $"Марш {meters:0} м — не замкнут, захвата нет"));

        CoalesceOwnTerritories(world, player.Id);

        world.Bump();
        return events;
        }
    }

    private bool TryArcCapture(
        Player player, List<GeoPoint> points, double meters,
        List<EngineEvent> events, out GeoShape? arcShape, out EngineEvent? error)
    {
        arcShape = null;
        error = null;
        if (meters < 120) return false;

        var world = W;
        var start = points[0];
        var end = points[^1];

        var anchor =
            OwnTerritoryCovering(player.Id, start) ??
            OwnTerritoryCovering(player.Id, end) ??
            world.TerritoriesOf(player.Id)
                .FirstOrDefault(t => _geo.DistanceMeters(
                    new[] { GeoShape.Simple(new List<GeoPoint> { start, start, start }) },
                    PartsAsShapes(t)) <= MergeToleranceM) ??
            world.TerritoriesOf(player.Id)
                .FirstOrDefault(t => _geo.DistanceMeters(
                    new[] { GeoShape.Simple(new List<GeoPoint> { end, end, end }) },
                    PartsAsShapes(t)) <= MergeToleranceM);

        if (anchor is null) return false;

        var ring = points.ToList();
        ring.Add(ring[0]);
        if (!_geo.TryBuildShape(ring, MinLoopAreaM2, out var shape, out var buildError))
        {
            error = new EngineEvent("warn", $"Дуга не принята: {buildError}");
            return false;
        }

        arcShape = shape;
        return true;
    }

    private void CoalesceOwnTerritories(World world, Guid playerId)
    {
        var owned = world.TerritoriesOf(playerId).ToList();
        var again = true;
        while (again)
        {
            again = false;
            for (var i = 0; i < owned.Count && !again; i++)
            {
                for (var j = i + 1; j < owned.Count && !again; j++)
                {
                    var a = owned[i];
                    var b = owned[j];
                    if (_geo.DistanceMeters(PartsAsShapes(a), PartsAsShapes(b)) > MergeToleranceM) continue;
                    var target = AreaKm2(a) >= AreaKm2(b) ? a : b;
                    var other = target == a ? b : a;
                    var merged = _geo.Union(PartsAsShapes(target).Concat(PartsAsShapes(other)));
                    var beforeArmy = target.Army + other.Army;
                    foreach (var o in owned.Where(x => x.Id == other.Id).ToList()) world.Territories.Remove(o);
                    owned.Remove(other);
                    owned.Remove(target);
                    target.Parts = ToParts(merged);
                    target.Army = beforeArmy;
                    world.Log("capture", $"UC-04 · Смежные земли объединены в «{target.Name}»: {AreaKm2(target):0.00} км², армия {target.Army}, лимит {ArmyLimitOf(target)}");
                    owned.Add(target);
                    again = true;
                }
            }
        }
    }

    private void VisitWarPoints(Guid playerId, List<GeoPoint> points, List<EngineEvent> events)
    {
        var world = W;
        foreach (var war in world.ActiveWars.Where(w => w.IsParticipant(playerId)).ToList())
        {
            var taken = 0;
            foreach (var p in war.Points.Where(p => p.CapturedBy != playerId))
            {
                foreach (var tp in points)
                {
                    if (GeoMath.ApproxDistanceM(p.Position, tp) > PointCaptureRadiusM) continue;
                    p.CapturedBy = playerId;
                    taken++;
                    break;
                }
            }
            if (taken == 0) continue;
            world.Log("war", $"UC-10.5 · {war.Name}: {world.PlayerById(playerId)?.Name} берёт {taken} очк(о/а) контроля");
            events.Add(new EngineEvent("war", $"Очки контроля +{taken} ({war.Name})"));
        }
    }

    private void ProcessCapture(Player player, GeoShape loop, List<EngineEvent> events)
    {
        var world = W;

        foreach (var territory in world.Territories.Where(t => t.OwnerId != player.Id).ToList())
        {
            var parts = PartsAsShapes(territory);
            var zone = _geo.Intersection(loop, parts);
            var zoneKm2 = _geo.AreaKm2(zone);
            if (zoneKm2 * 1e6 < MinZoneAreaM2) continue;

            var activeWar = world.ActiveWars.FirstOrDefault(w => w.ContextTerritoryId == territory.Id);
            if (activeWar != null)
            {
                if (!activeWar.IsParticipant(player.Id)) JoinWar(player, activeWar, events);
                continue;
            }

            StartWar(player, territory, zone, zoneKm2, events);
        }

        var otherLand = world.Territories
            .Where(t => t.OwnerId != player.Id)
            .SelectMany(PartsAsShapes)
            .ToList();
        var freeAll = _geo.Difference(new[] { loop }, otherLand);
        var free = freeAll
            .Where(s => _geo.AreaKm2(new[] { s }) * 1e6 >= MinLoopAreaM2)
            .ToList();
        var freeKm2 = _geo.AreaKm2(free);
        if (freeKm2 * 1e6 < MinPieceAreaM2) return;

        var owned = world.TerritoriesOf(player.Id).ToList();
        var touched = owned
            .Where(t => _geo.DistanceMeters(new[] { loop }, PartsAsShapes(t)) <= MergeToleranceM)
            .ToList();
        if (touched.Count == 0)
        {
            var t = new Territory
            {
                OwnerId = player.Id,
                Name = world.NextTerritoryName("Сектор"),
                Parts = ToParts(free),
                CreatedAtGameTime = world.GameNow,
            };
            world.Territories.Add(t);
            world.Log("capture", $"UC-01 · Захвачено {freeKm2:0.00} км² — новая территория «{t.Name}» (лимит {ArmyLimitOf(t)})");
            events.Add(new EngineEvent("capture", $"Новая территория «{t.Name}»: {freeKm2:0.00} км²"));
        }
        else
        {
            var target = touched.OrderByDescending(AreaKm2).First();
            var absorbed = touched.Where(t => t.Id != target.Id).ToList();
            var merged = _geo.Union(
                PartsAsShapes(target).Concat(free).Concat(absorbed.SelectMany(PartsAsShapes)));
            var addedArmy = absorbed.Sum(t => t.Army);
            foreach (var other in absorbed) world.Territories.Remove(other);
            target.Parts = ToParts(merged);
            target.Army += addedArmy;
            var newArea = AreaKm2(target);
            world.Log("capture", $"UC-04 · Объединение в «{target.Name}»: {newArea:0.00} км², армия {target.Army}, лимит {ArmyLimitOf(target)}");
            events.Add(new EngineEvent("capture", $"Территории объединены: {newArea:0.00} км² (лимит {ArmyLimitOf(target)})"));
        }
    }

    // --------------------------------------------------------------- UC-06/07: войны

    private void StartWar(Player attacker, Territory defender, List<GeoShape> zone, double zoneKm2, List<EngineEvent> events)
    {
        var world = W;
        var defTotal = defender.Army;
        var defMob = WarPolicy.DefenderMobilization(defender.Army, zoneKm2, AreaKm2(defender));
        var source = PickCommitSource(attacker.Id, zone);
        if (source is null)
        {
            events.Add(new EngineEvent("warn", "Нет войск для атаки — война не началась"));
            return;
        }

        var atkTotal = source.Army;
        var atkMob = Math.Max(1, Math.Min(source.Army, (int)Math.Floor(source.Army * WarPolicy.AttackerCommitShare)));
        source.Army -= atkMob;
        defender.Army -= defMob;

        var war = new War
        {
            Name = world.NextTerritoryName("Война №"),
            ZoneParts = ToParts(zone),
            ContextTerritoryId = defender.Id,
            ContextOwnerAtStart = defender.OwnerId,
            ZoneAreaKm2 = zoneKm2,
            StartedAtGameTime = world.GameNow,
            LastJoinAtGameTime = world.GameNow,
            NextCycleAtGameTime = world.GameNow + WarPolicy.CyclePeriod,
            PointsPerCycle = WarPolicy.PointsPerCycle(zoneKm2),
        };
        war.Sides.Add(new WarSide { PlayerId = attacker.Id, Mobilized = atkMob, SourceTerritoryId = source.Id });
        war.Sides.Add(new WarSide { PlayerId = defender.OwnerId, Mobilized = defMob, SourceTerritoryId = defender.Id });
        GeneratePoints(war);
        world.Wars.Add(war);

        world.Log("war", $"UC-06 · {war.Name}: {attacker.Name} вторгается на земли {world.PlayerById(defender.OwnerId)?.Name}. " +
                         $"Зона {zoneKm2:0.00} км². В войну ушли {atkMob} из {atkTotal} атакующих и {defMob} из {defTotal} обороняющихся; остальная армия остаётся вне войны");
        events.Add(new EngineEvent("war", $"ВОЙНА! {war.Name}: зона {zoneKm2:0.00} км², ваши войска в зоне: {atkMob}"));
    }

    private void JoinWar(Player joiner, War war, List<EngineEvent> events)
    {
        var world = W;
        var zone = ShapesOf(war.ZoneParts);
        var source = PickCommitSource(joiner.Id, zone);
        if (source is null)
        {
            events.Add(new EngineEvent("warn", "Нет войск, чтобы вступить в войну"));
            return;
        }
        var mob = Math.Max(1, Math.Min(source.Army, (int)Math.Floor(source.Army * WarPolicy.AttackerCommitShare)));
        source.Army -= mob;
        war.Sides.Add(new WarSide { PlayerId = joiner.Id, Mobilized = mob, SourceTerritoryId = source.Id });
        war.LastJoinAtGameTime = world.GameNow;
        world.Log("war", $"UC-07 · {joiner.Name} вступает в {war.Name} третьей стороной: мобилизовано {mob}. Отсчёт 24 ч перезапущен");
        events.Add(new EngineEvent("war", $"{war.Name}: вступление третьей стороной, отсчёт перезапущен"));
    }

    // --------------------------------------------------------------- UC-08/09/13: действия игрока

    public List<EngineEvent> SendReinforcements(Guid playerId, Guid warId, Guid fromTerritoryId, int count)
    {
        var events = new List<EngineEvent>();
        var world = W;
        lock (world.Gate)
        {
        var war = world.WarById(warId);
        var from = world.TerritoryById(fromTerritoryId);
        if (war is null || war.Ended || from is null || from.OwnerId != playerId) return events;
        var side = war.SideOf(playerId);
        if (side is null) return events;
        count = Math.Clamp(count, 0, from.Army);
        if (count <= 0)
        {
            events.Add(new EngineEvent("warn", "Нечего отправлять"));
            return events;
        }

        var zone = ShapesOf(war.ZoneParts);
        var adjacent = _geo.DistanceMeters(PartsAsShapes(from), zone) <= WarPolicy.AdjacentReinforceRangeM;
        if (!adjacent && !from.HasBuilding(BuildingType.Airport))
        {
            events.Add(new EngineEvent("warn", "Территория не смежна с зоной войны (нужен аэропорт)"));
            return events;
        }

        from.Army -= count;
        side.Mobilized += count;
        world.Log("war", $"UC-08 · Подкрепление в {war.Name}: +{count} солдат из «{from.Name}» ({(adjacent ? "смежная территория" : "аэропорт")})");
        events.Add(new EngineEvent("war", $"Подкрепление отправлено: +{count} ({war.Name})"));
        world.Bump();
        return events;
        }
    }

    public List<EngineEvent> Retreat(Guid playerId, Guid warId, int count)
    {
        var events = new List<EngineEvent>();
        var world = W;
        lock (world.Gate)
        {
        var war = world.WarById(warId);
        if (war is null || war.Ended) return events;
        var side = war.SideOf(playerId);
        if (side is null) return events;
        count = Math.Clamp(count, 0, side.Mobilized);
        if (count <= 0)
        {
            events.Add(new EngineEvent("warn", "Нет войск для отступления"));
            return events;
        }

        var survivors = (int)Math.Round(count * (1.0 - CombatPolicy.RetreatLossFactor));
        side.Mobilized -= count;
        var target =
            (world.TerritoryById(side.SourceTerritoryId) is { } src && src.OwnerId == playerId) ? src
            : NearestOwn(playerId, _geo.Centroid(ShapesOf(war.ZoneParts)));

        if (target is null)
        {
            world.Log("war", $"UC-09 · {war.Name}: отступление {world.PlayerById(playerId)?.Name} — выжившие {survivors} распущены (нет земель)");
        }
        else
        {
            var before = target.Army;
            target.Army = Math.Min(ArmyLimitOf(target), target.Army + survivors);
            survivors = target.Army - before;
            world.Log("war", $"UC-09 · {war.Name}: отступление — выведено {count}, вернулось {survivors} в «{target.Name}», потери {count - survivors}");
        }
        events.Add(new EngineEvent("war", $"Отступление: выведено {count}, вернулось {survivors} (потери {count - survivors})"));
        world.Bump();
        return events;
        }
    }

    public List<EngineEvent> TransferTroops(Guid playerId, Guid fromId, Guid toId, int count)
    {
        var events = new List<EngineEvent>();
        var world = W;
        lock (world.Gate)
        {
        var from = world.TerritoryById(fromId);
        var to = world.TerritoryById(toId);
        if (from is null || to is null || from.OwnerId != playerId || to.OwnerId != playerId || from.Id == toId) return events;
        if (!from.HasBuilding(BuildingType.Airport) && !to.HasBuilding(BuildingType.Airport))
        {
            events.Add(new EngineEvent("warn", "Переброска войск требует аэропорта на одной из территорий"));
            return events;
        }
        count = Math.Clamp(count, 0, from.Army);
        if (count <= 0)
        {
            events.Add(new EngineEvent("warn", "Нечего перебрасывать"));
            return events;
        }
        from.Army -= count;
        to.Army += count;
        world.Log("system", $"Аэропорт: переброска {count} солдат «{from.Name}» → «{to.Name}»");
        events.Add(new EngineEvent("system", $"Переброшено {count} солдат по воздуху"));
        world.Bump();
        return events;
        }
    }

    public List<EngineEvent> Build(Guid playerId, Guid territoryId, BuildingType type)
    {
        var events = new List<EngineEvent>();
        var world = W;
        lock (world.Gate)
        {
        var t = world.TerritoryById(territoryId);
        if (t is null || t.OwnerId != playerId) return events;
        var spec = BuildingCatalog.All[type];
        if (t.Buildings.Count(b => b.Type == type) >= spec.MaxPerTerritory)
        {
            events.Add(new EngineEvent("warn", $"Лимит построек «{spec.NameRu}» исчерпан"));
            return events;
        }
        if (t.Army < spec.Cost)
        {
            events.Add(new EngineEvent("warn", $"Недостаточно армии для «{spec.NameRu}»: нужно {spec.Cost}, есть {t.Army}"));
            return events;
        }
        t.Army -= spec.Cost;
        t.Buildings.Add(new Building { Type = type, BuiltAtGameTime = world.GameNow });
        world.Log("build", $"UC-13 · «{t.Name}»: построен {spec.NameRu} (−{spec.Cost} солдат). {spec.DescRu}");
        events.Add(new EngineEvent("build", $"{spec.NameRu} построен на «{t.Name}»"));
        world.Bump();
        return events;
        }
    }

    // --------------------------------------------------------------- UC-10/10.5/11/12/14: тик времени

    public void Tick(TimeSpan gameDelta)
    {
        var world = W;
        lock (world.Gate)
        {
        world.GameNow += gameDelta;
        _hourAccumulator += gameDelta.TotalHours;
        while (_hourAccumulator >= 1.0)
        {
            _hourAccumulator -= 1.0;
            HourStep();
        }
        world.Bump();
        }
    }

    private void HourStep()
    {
        var world = W;

        foreach (var war in world.ActiveWars.ToList())
        {
            while (war.NextCycleAtGameTime <= world.GameNow)
            {
                GeneratePoints(war);
                war.NextCycleAtGameTime += WarPolicy.CyclePeriod;
            }

            var active = war.Sides.Where(s => s.Mobilized > 0).ToList();
            if (war.Sides.Count >= 2 && active.Count <= 1)
            {
                var last = active.FirstOrDefault();
                var winner = last?.PlayerId ?? BestSide(war).Side?.PlayerId;
                EndWar(war, winner, last != null ? "у противников не осталось войск (UC-12)" : "все стороны обескровлены", null);
                continue;
            }

            ApplyCombatHour(war);

            if (!war.Ended && world.GameNow >= war.LastJoinAtGameTime + WarPolicy.JoinRestart)
            {
                EndWar(war, BestSide(war).Side?.PlayerId, "истёк 24-часовой срок (UC-11)", null);
            }
        }

        foreach (var t in world.Territories.ToList())
        {
            if (t.Army <= 0)
            {
                t.DecayDebt = 0;
                continue;
            }
            var debt = t.DecayDebt + DecayPolicy.HourlyDecay(ArmyLimitOf(t), t.HasBuilding(BuildingType.Barracks));
            var take = (int)Math.Floor(debt);
            t.DecayDebt = debt - take;
            t.Army = Math.Max(0, t.Army - take);
        }

        _bots.Hourly(world, this, _rng);
    }

    private void ApplyCombatHour(War war)
    {
        var world = W;
        var sides = war.Sides.Where(s => s.Mobilized > 0).ToList();
        if (sides.Count < 2) return;

        var strengths = new List<double>();
        foreach (var side in sides)
        {
            var bonus = HasFortressNearZone(side.PlayerId, war) ? CombatPolicy.FortressDefenseBonus : 0.0;
            strengths.Add(CombatPolicy.Strength(side.Mobilized, bonus));
        }
        var totalF = strengths.Sum();

        var report = new List<string>();
        for (var i = 0; i < sides.Count; i++)
        {
            var loss = CombatPolicy.HourlyLoss(sides[i].Mobilized, strengths[i], totalF);
            if (loss <= 0) continue;
            sides[i].Mobilized -= loss;
            var name = world.PlayerById(sides[i].PlayerId)?.Name ?? "?";
            report.Add($"{name} −{loss}");
        }
        if (report.Count > 0)
            world.Log("battle", $"UC-10 · {war.Name}: столкновения — {string.Join(", ", report)}");
    }

    private void GeneratePoints(War war)
    {
        var world = W;
        war.CyclesGenerated++;
        var samples = _geo.PerimeterSample(ShapesOf(war.ZoneParts), war.PointsPerCycle, _rng);
        foreach (var pos in samples)
            war.Points.Add(new WarPoint { Position = pos, Cycle = war.CyclesGenerated });
        world.Log("war", $"UC-10.5 · {war.Name}: волна {war.CyclesGenerated} — {war.PointsPerCycle} точек контроля на периметре зоны");
    }

    private void EndWar(War war, Guid? winnerId, string reason, List<EngineEvent>? events)
    {
        var world = W;
        war.Ended = true;
        war.WinnerId = winnerId;
        war.EndReason = reason;

        var winner = winnerId.HasValue ? world.PlayerById(winnerId.Value) : null;
        var scoreStr = SideScoreStr(war, winnerId);

        Territory? zoneHome = null;
        var ctx = world.TerritoryById(war.ContextTerritoryId);
        if (ctx is not null && winnerId.HasValue && winnerId.Value != war.ContextOwnerAtStart)
        {
            var zoneShapes = ShapesOf(war.ZoneParts);
            var remaining = _geo.Difference(PartsAsShapes(ctx), zoneShapes);
            var comps = _geo.ConnectedComponents(remaining);

            if (comps.Count == 0)
            {
                world.Territories.Remove(ctx);
                world.Log("system", $"«{ctx.Name}» поглощена победителем ({winner?.Name}) целиком");
            }
            else
            {
                var areas = comps.Select(g => _geo.AreaKm2(g)).ToList();
                for (var i = comps.Count - 1; i >= 0; i--)
                {
                    if (areas[i] * 1e6 >= MinSplitPieceM2) continue;
                    if (comps.Count == 1) break;
                    var nearest = -1;
                    var bestScore = double.MaxValue;
                    for (var j = 0; j < comps.Count; j++)
                    {
                        if (j == i) continue;
                        var bigBonus = areas[j] * 1e6 >= MinSplitPieceM2 ? 0.0 : 1e9;
                        var d = bigBonus + _geo.DistanceMeters(comps[i], comps[j]);
                        if (d < bestScore)
                        {
                            bestScore = d;
                            nearest = j;
                        }
                    }
                    if (nearest < 0) continue;
                    comps[nearest].AddRange(comps[i]);
                    areas[nearest] += areas[i];
                    comps.RemoveAt(i);
                    areas.RemoveAt(i);
                }
                if (comps.Count == 0)
                {
                    world.Territories.Remove(ctx);
                    return;
                }
                var totalArea = areas.Sum();
                var armyLeft = ctx.Army;
                var shares = areas.Select(a => (int)Math.Round(armyLeft * a / totalArea)).ToList();
                var drift = armyLeft - shares.Sum();

                ctx.Parts = ToParts(comps[0]);
                ctx.Army = shares[0];
                var biggestShare = shares[0];
                Territory? biggest = ctx;
                for (var i = 1; i < comps.Count; i++)
                {
                    var piece = new Territory
                    {
                        OwnerId = ctx.OwnerId,
                        Name = world.NextTerritoryName("Осколок"),
                        Parts = ToParts(comps[i]),
                        Army = shares[i],
                        CreatedAtGameTime = world.GameNow,
                    };
                    world.Territories.Add(piece);
                    if (shares[i] > biggestShare)
                    {
                        biggestShare = shares[i];
                        biggest = piece;
                    }
                }
                if (drift != 0 && biggest is not null) biggest.Army += drift;
                world.Log("war", $"UC-05 · «{ctx.Name}» распалась на {comps.Count} частей, армия {armyLeft} разделена по площади: {string.Join(" / ", shares)}");
                if (ctx.Parts.Count == 0) world.Territories.Remove(ctx);
            }

            var winTouched = world.TerritoriesOf(winnerId.Value)
                .Where(t => _geo.DistanceMeters(PartsAsShapes(t), zoneShapes) <= MergeToleranceM)
                .ToList();
            if (winTouched.Count > 0)
            {
                var target = winTouched.OrderByDescending(AreaKm2).First();
                var absorb = winTouched.Where(t => t.Id != target.Id).ToList();
                var merged = _geo.Union(
                    PartsAsShapes(target).Concat(zoneShapes).Concat(absorb.SelectMany(PartsAsShapes)));
                var absorbedArmy = absorb.Sum(t => t.Army);
                foreach (var other in absorb) world.Territories.Remove(other);
                target.Parts = ToParts(merged);
                target.Army += absorbedArmy;
                zoneHome = target;
                world.Log("capture", $"UC-04 · Зона войны влита в «{target.Name}» ({AreaKm2(target):0.00} км²)");
            }
            else
            {
                var t = new Territory
                {
                    OwnerId = winnerId.Value,
                    Name = world.NextTerritoryName("Трофей"),
                    Parts = ToParts(zoneShapes),
                    CreatedAtGameTime = world.GameNow,
                };
                world.Territories.Add(t);
                zoneHome = t;
            }
        }

        foreach (var side in war.Sides.Where(s => s.Mobilized > 0).ToList())
        {
            var playerId = side.PlayerId;
            var mob = side.Mobilized;
            Territory? target;
            if (playerId == winnerId && zoneHome is not null)
            {
                target = zoneHome;
                target.Army += mob;
            }
            else
            {
                var src = world.TerritoryById(side.SourceTerritoryId);
                target = (src is not null && src.OwnerId == playerId)
                    ? src
                    : NearestOwn(playerId, _geo.Centroid(ShapesOf(war.ZoneParts)));
                if (target is not null)
                {
                    var before = target.Army;
                    target.Army = Math.Min(ArmyLimitOf(target), target.Army + mob);
                    mob = target.Army - before;
                    if (mob < side.Mobilized)
                        world.Log("system", $"{world.PlayerById(playerId)?.Name}: {side.Mobilized - mob} выживших сверх лимита распущены");
                }
            }
            if (target is null)
                world.Log("system", $"{world.PlayerById(playerId)?.Name}: выжившие войска распущены (нет земель)");
            else
                world.Log("war", $"{war.Name} завершение: {world.PlayerById(playerId)?.Name} возвращает {mob} выживших в «{target.Name}»");
            side.Mobilized = 0;
        }

        world.Log("war", $"UC-11/12 · {war.Name} завершена ({reason}). Победитель: {winner?.Name ?? "—"} [{scoreStr}]. Контроль передан");
        events?.Add(new EngineEvent("war", $"{war.Name} завершена — победа: {winner?.Name ?? "—"}"));
    }

    // --------------------------------------------------------------- сервис для ботов и UI

    public void BotVisitPoints(War war, Guid playerId, int count)
    {
        lock (W.Gate)
        {
            var free = war.Points.Where(p => p.CapturedBy != playerId).ToList();
            var n = Math.Min(Math.Max(count, 0), free.Count);
            for (var i = 0; i < n; i++) free[i].CapturedBy = playerId;
            W.Bump();
        }
    }

    public List<ReinforceOption> ReinforceOptions(Guid playerId, Guid warId)
    {
        var world = W;
        lock (world.Gate)
        {
            var war = world.WarById(warId);
            var options = new List<ReinforceOption>();
            if (war is null) return options;
            var zone = ShapesOf(war.ZoneParts);
            foreach (var t in world.TerritoriesOf(playerId).Where(t => t.Army > 0))
            {
                var d = _geo.DistanceMeters(PartsAsShapes(t), zone);
                options.Add(new ReinforceOption(t.Id, t.Name, t.Army, d, d <= WarPolicy.AdjacentReinforceRangeM, t.HasBuilding(BuildingType.Airport)));
            }
            return options.OrderBy(o => o.DistanceM).ToList();
        }
    }

    public double WarZoneKm2(Guid warId) =>
        W.WarById(warId) is { } w ? _geo.AreaKm2(ShapesOf(w.ZoneParts)) : 0;

    // --------------------------------------------------------------- хелперы

    public List<GeoShape> PartsAsShapes(Territory t) =>
        t.Parts.Select(p => new GeoShape(p.Exterior, p.Holes)).ToList();

    private List<GeoShape> ShapesOf(List<TerritoryPart> parts) =>
        parts.Select(p => new GeoShape(p.Exterior, p.Holes)).ToList();

    private List<TerritoryPart> ToParts(IEnumerable<GeoShape> shapes) => shapes
        .Where(s => s.Exterior.Count >= 3)
        .Select(s => new TerritoryPart { Exterior = s.Exterior.ToList(), Holes = s.Holes.Select(h => h.ToList()).ToList() })
        .ToList();

    public double AreaKm2(Territory t) => _geo.AreaKm2(PartsAsShapes(t));

    public int ArmyLimitOf(Territory t) => ArmyPolicy.ArmyLimitFor(AreaKm2(t));

    private Territory? OwnTerritoryCovering(Guid playerId, GeoPoint p) =>
        W.Territories.FirstOrDefault(t => t.OwnerId == playerId && _geo.CoversPoint(PartsAsShapes(t), p));

    private Territory? LargestOwn(Guid playerId) =>
        W.TerritoriesOf(playerId).OrderByDescending(AreaKm2).FirstOrDefault();

    private Territory? NearestOwn(Guid playerId, GeoPoint p)
    {
        var probe = GeoShape.Simple(new List<GeoPoint> { p, p, p });
        return W.TerritoriesOf(playerId)
            .OrderBy(t => _geo.DistanceMeters(new[] { probe }, PartsAsShapes(t)))
            .FirstOrDefault();
    }

    private Territory? PickCommitSource(Guid playerId, List<GeoShape> zone)
    {
        var owned = W.TerritoriesOf(playerId).Where(t => t.Army > 0).ToList();
        var adjacent = owned
            .Where(t => _geo.DistanceMeters(PartsAsShapes(t), zone) <= WarPolicy.AdjacentReinforceRangeM)
            .OrderByDescending(ArmyLimitOf)
            .FirstOrDefault();
        return adjacent ?? owned.OrderByDescending(t => t.Army).FirstOrDefault();
    }

    private bool HasFortressNearZone(Guid playerId, War war)
    {
        var zone = ShapesOf(war.ZoneParts);
        return W.TerritoriesOf(playerId)
            .Any(t => t.HasBuilding(BuildingType.Fortress) &&
                      _geo.DistanceMeters(PartsAsShapes(t), zone) <= FortressRadiusM);
    }

    private (WarSide? Side, double H, double S, double C) BestSide(War war)
    {
        WarSide? best = null;
        var bestC = double.MinValue;
        double bh = 0, bs = 0;
        foreach (var side in war.Sides)
        {
            var (h, s, c) = SideScore(war, side);
            if (!(c > bestC)) continue;
            bestC = c;
            best = side;
            bh = h;
            bs = s;
        }
        return (best, bh, bs, bestC);
    }

    private (double H, double S, double C) SideScore(War war, WarSide side)
    {
        var possible = Math.Max(1, war.PointsPerCycle * Math.Max(1, war.CyclesGenerated));
        var myPts = war.Points.Count(p => p.CapturedBy == side.PlayerId);
        var all = war.Sides.Sum(s => s.Mobilized);
        return ControlPolicy.Score(myPts, possible, side.Mobilized, all);
    }

    private string SideScoreStr(War war, Guid? playerId)
    {
        if (playerId is null) return "C=—";
        var side = war.SideOf(playerId.Value);
        if (side is null) return "C=—";
        var (h, s, c) = SideScore(war, side);
        return $"C={c:0.00} · H={h:0.00} · S={s:0.00}";
    }
}
