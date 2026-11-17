namespace SanyTotalWar.Domain.Entities;

using SanyTotalWar.Domain.Common;

public sealed class Building
{
    public BuildingType Type { get; init; }
    public DateTime BuiltAtGameTime { get; init; }

    /// <summary>Точка установки постройки; null = центр территории (наследие).</summary>
    public GeoPoint? Position { get; init; }
}

public enum BuildingType
{
    ConscriptionOffice,
    Fortress,
    Airport,
    Barracks,
}

public sealed record BuildingSpec(
    BuildingType Type,
    string NameRu,
    string DescRu,
    int Cost,
    int MaxPerTerritory);

public static class BuildingCatalog
{
    public static readonly IReadOnlyDictionary<BuildingType, BuildingSpec> All =
        new Dictionary<BuildingType, BuildingSpec>
        {
            [BuildingType.ConscriptionOffice] = new(
                BuildingType.ConscriptionOffice, "Военкомат", "+20% войск за ходьбу", 300, 1),
            [BuildingType.Fortress] = new(
                BuildingType.Fortress, "Крепость", "+20% силы обороны войск в радиусе 500 м (не стакается)", 500, int.MaxValue),
            [BuildingType.Airport] = new(
                BuildingType.Airport, "Аэропорт", "Позволяет перебрасывать войска в любую свою территорию", 1000, 1),
            [BuildingType.Barracks] = new(
                BuildingType.Barracks, "Казармы", "Пассивное уменьшение армии меньше на 50%", 500, 1),
        };
}
