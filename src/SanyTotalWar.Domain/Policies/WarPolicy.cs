namespace SanyTotalWar.Domain.Policies;

public static class WarPolicy
{
    public const double AttackerCommitShare = 0.25;
    public const double AdjacentReinforceRangeM = 2500.0;
    public static readonly TimeSpan JoinRestart = TimeSpan.FromHours(24);
    public static readonly TimeSpan CyclePeriod = TimeSpan.FromHours(12);

    public static int DefenderMobilization(int defenderArmy, double zoneAreaKm2, double totalAreaKm2)
    {
        if (defenderArmy <= 0 || totalAreaKm2 <= 0) return 0;
        return (int)Math.Floor(defenderArmy * zoneAreaKm2 / totalAreaKm2);
    }

    public static int PointsPerCycle(double zoneAreaKm2) => Math.Max(3, (int)Math.Ceiling(4 * zoneAreaKm2));
}
