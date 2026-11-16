namespace SanyTotalWar.Domain.Policies;

public static class ControlPolicy
{
    public static (double H, double S, double C) Score(
        int myPoints, int totalPossiblePoints, int myArmy, int allArmies)
    {
        var h = totalPossiblePoints > 0 ? (double)myPoints / totalPossiblePoints : 0.0;
        var s = allArmies > 0 ? (double)myArmy / allArmies : 0.0;
        return (h, s, 0.5 * h + 0.5 * s);
    }
}
