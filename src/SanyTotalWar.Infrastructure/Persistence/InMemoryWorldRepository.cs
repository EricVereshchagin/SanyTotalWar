namespace SanyTotalWar.Infrastructure.Persistence;

using SanyTotalWar.Application.Abstractions;
using SanyTotalWar.Application;
using SanyTotalWar.Domain;

public sealed class InMemoryWorldRepository : IWorldRepository
{
    public World World { get; }

    public InMemoryWorldRepository()
    {
        World = new World();
        WorldSeeder.Seed(World);
    }
}
