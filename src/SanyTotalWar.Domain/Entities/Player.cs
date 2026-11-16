namespace SanyTotalWar.Domain.Entities;

public sealed class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public bool IsBot { get; init; }
    public string ColorHex { get; init; } = "#f0b429";
}
