namespace SanyTotalWar.Domain.Entities;

using SanyTotalWar.Domain.Common;

public sealed class WarSide
{
    public Guid PlayerId { get; init; }
    public int Mobilized { get; set; }
    public Guid SourceTerritoryId { get; set; }
    public bool Eliminated { get; set; }
}

public sealed class WarPoint
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GeoPoint Position { get; init; }
    public int Cycle { get; init; }
    public Guid? CapturedBy { get; set; }
}

public sealed class War
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<TerritoryPart> ZoneParts { get; set; } = new();
    public Guid ContextTerritoryId { get; init; }
    public Guid ContextOwnerAtStart { get; init; }
    public double ZoneAreaKm2 { get; init; }
    public List<WarSide> Sides { get; } = new();
    public List<WarPoint> Points { get; } = new();
    public int PointsPerCycle { get; set; }
    public int CyclesGenerated { get; set; }
    public DateTime StartedAtGameTime { get; init; }
    public DateTime LastJoinAtGameTime { get; set; }
    public DateTime NextCycleAtGameTime { get; set; }
    public bool Ended { get; set; }
    public Guid? WinnerId { get; set; }
    public string? EndReason { get; set; }

    public WarSide? SideOf(Guid playerId) => Sides.Find(s => s.PlayerId == playerId);
    public bool IsParticipant(Guid playerId) => Sides.Exists(s => s.PlayerId == playerId);
}
