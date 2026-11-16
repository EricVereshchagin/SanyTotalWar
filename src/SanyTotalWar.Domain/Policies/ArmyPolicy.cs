namespace SanyTotalWar.Domain.Policies;

using SanyTotalWar.Domain.Entities;

public static class ArmyPolicy
{
    public const double MetersPerSoldier = 25.0;

    public static int ArmyLimitFor(double areaKm2) =>
        (int)Math.Round(200 * Math.Sqrt(Math.Max(areaKm2, 0) + 0.05));

    public static int SoldiersForWalk(double meters, double bonusMul = 1.0) =>
        (int)(meters / MetersPerSoldier * bonusMul);

    public static double WalkBonusMul(Territory territory) =>
        territory.HasBuilding(BuildingType.ConscriptionOffice) ? 1.2 : 1.0;

    public static int BuildingCountByType(Territory territory, BuildingType type) =>
        territory.CountBuildings(type);
}
