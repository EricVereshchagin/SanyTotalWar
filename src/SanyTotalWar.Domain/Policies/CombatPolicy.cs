namespace SanyTotalWar.Domain.Policies;

public static class CombatPolicy
{
    public const double FortressDefenseBonus = 0.2;
    public const double RetreatLossFactor = 0.5;
    public const double BaseHourlyLossRate = 0.02;

    public static double Strength(int army, double bonus) => army * (1.0 + bonus);

    public static double LossCoefficientK(double selfStrength, double totalStrength)
    {
        if (selfStrength <= 0) return double.MaxValue;
        var k = totalStrength / selfStrength;
        return Math.Max(1.0, k);
    }

    public static int HourlyLoss(int army, double selfStrength, double totalStrength)
    {
        if (army <= 0) return 0;
        var k = LossCoefficientK(selfStrength, totalStrength);
        var loss = Math.Ceiling(army * BaseHourlyLossRate * k);
        return (int)Math.Min(army, loss);
    }
}
