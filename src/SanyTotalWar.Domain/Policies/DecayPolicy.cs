namespace SanyTotalWar.Domain.Policies;

public static class DecayPolicy
{
    public const double DailyShare = 0.05;

    public static double HourlyDecay(int armyLimit, bool hasBarracks)
    {
        var mult = hasBarracks ? 0.5 : 1.0;
        return armyLimit * DailyShare / 24.0 * mult;
    }
}
