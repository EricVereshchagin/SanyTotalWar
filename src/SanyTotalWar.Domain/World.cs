namespace SanyTotalWar.Domain;

using SanyTotalWar.Domain.Common;
using SanyTotalWar.Domain.Entities;

public sealed class JournalEntry
{
    public required DateTime GameTime { get; init; }
    public required string Kind { get; init; }
    public required string Message { get; init; }
}

public sealed class TrackMark
{
    public required Guid PlayerId { get; init; }
    public required List<GeoPoint> Points { get; init; }
    public required DateTime AtGameTime { get; init; }
    public required bool Loop { get; init; }
}

public sealed class World
{
    public readonly object Gate = new();
    public List<Player> Players { get; } = new();
    public List<Territory> Territories { get; } = new();
    public List<War> Wars { get; } = new();
    public List<JournalEntry> Journal { get; } = new();
    public List<TrackMark> RecentTracks { get; } = new();
    public DateTime GameStart { get; set; }
    public DateTime GameNow { get; set; }
    public long Version { get; private set; }
    public int NextTerritoryIndex { get; set; } = 1;

    public string NextTerritoryName(string prefix) => $"{prefix} {NextTerritoryIndex++}";

    public void Bump() => Version++;

    public void Log(string kind, string message)
    {
        lock (Gate)
        {
            Journal.Add(new JournalEntry { GameTime = GameNow, Kind = kind, Message = message });
            if (Journal.Count > 500) Journal.RemoveRange(0, Journal.Count - 500);
        }
        Bump();
    }

    public List<JournalEntry> JournalSnapshot(int last = 60)
    {
        lock (Gate)
        {
            return Journal.TakeLast(last).ToList();
        }
    }

    public Player? PlayerById(Guid id) => Players.Find(p => p.Id == id);
    public Territory? TerritoryById(Guid id) => Territories.Find(t => t.Id == id);
    public War? WarById(Guid id) => Wars.Find(w => w.Id == id);
    public IEnumerable<War> ActiveWars => Wars.Where(w => !w.Ended);
    public IEnumerable<Territory> TerritoriesOf(Guid playerId) =>
        Territories.Where(t => t.OwnerId == playerId);
}
