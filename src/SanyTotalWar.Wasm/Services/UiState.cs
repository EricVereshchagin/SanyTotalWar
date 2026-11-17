namespace SanyTotalWar.Wasm.Services;

using SanyTotalWar.Application;

public sealed class UiState
{
    private readonly GameEngine _engine;

    public UiState(GameEngine engine)
    {
        _engine = engine;
        MeId = engine.W.Players.First(p => !p.IsBot).Id;
    }

    public Guid MeId { get; }
    public Guid? SelectedTerritoryId { get; set; }
    public double GameSpeed { get; set; } = 600;
    public bool ShowBriefing { get; set; } = true;
    public bool MarchMode { get; set; }
    public bool DockOpen { get; set; } = true;
    public string DockTab { get; set; } = "dossier";
    private readonly List<ToastItem> _toasts = new();
    public IReadOnlyList<ToastItem> Toasts => _toasts;

    public void PushToasts(IEnumerable<EngineEvent> events)
    {
        var now = DateTime.UtcNow;
        foreach (var e in events.Take(4))
            _toasts.Add(new ToastItem(Guid.NewGuid(), e.Kind, e.Message, now));
        while (_toasts.Count > 6) _toasts.RemoveAt(0);
    }

    public void TickToasts()
    {
        _toasts.RemoveAll(t => (DateTime.UtcNow - t.CreatedUtc).TotalSeconds > 7);
    }
}

public sealed record ToastItem(Guid Id, string Kind, string Message, DateTime CreatedUtc);
