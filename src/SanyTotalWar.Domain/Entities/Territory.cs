namespace SanyTotalWar.Domain.Entities;

using SanyTotalWar.Domain.Common;

public sealed class TerritoryPart
{
    public List<GeoPoint> Exterior { get; set; } = new();
    public List<List<GeoPoint>> Holes { get; set; } = new();
}

public sealed class Territory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public List<TerritoryPart> Parts { get; set; } = new();
    public int Army { get; set; }
    public List<Building> Buildings { get; set; } = new();
    public DateTime CreatedAtGameTime { get; init; }
    public double DecayDebt { get; set; }

    public int CountBuildings(BuildingType type) => Buildings.Count(b => b.Type == type);

    public bool HasBuilding(BuildingType type) => Buildings.Exists(b => b.Type == type);
}
