namespace SanyTotalWar.Web.Services;

using SanyTotalWar.Application;

public sealed class GameTickService : BackgroundService
{
    private readonly GameEngine _engine;
    private readonly UiState _ui;
    private readonly ILogger<GameTickService> _logger;

    public GameTickService(GameEngine engine, UiState ui, ILogger<GameTickService> logger)
    {
        _engine = engine;
        _ui = ui;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _engine.Tick(TimeSpan.FromSeconds(0.25 * _ui.GameSpeed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "game tick failed");
            }
        }
    }
}
