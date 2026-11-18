namespace SanyTotalWar.Wasm.Services;

using SanyTotalWar.Domain.Entities;

public sealed record TerrVm(
    Guid Id, string Name, Guid OwnerId, string OwnerName, string OwnerColor, bool My,
    double AreaKm2, int Limit, int Army, List<BuildingSpec> Buildings, int ActiveWars, bool Selected);

public sealed record SideVm(string Name, string Color, int Mob, double H, double S, double C, bool Me);

public sealed record WarVm(
    Guid Id, Guid TerritoryId, string Name, double ZoneKm2, List<SideVm> Sides, int MinutesLeft,
    int MyPoints, int PointsPossible, bool IAmSide, int MyMob,
    bool Ended, string? WinnerName, string? EndReason);

public sealed record JournalVm(DateTime Time, string Kind, string Message);

public sealed record WorldVm(
    long Version, DateTime Now, List<TerrVm> Territories, List<WarVm> Wars,
    List<JournalVm> Journal, int MyArmy, double MyArea, int MyLimit, Guid MyId, int MyActiveWars);
