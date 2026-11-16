using SanyTotalWar.Application;
using SanyTotalWar.Application.Abstractions;
using SanyTotalWar.Infrastructure.Geo;
using SanyTotalWar.Infrastructure.Persistence;
using SanyTotalWar.Web.Components;
using SanyTotalWar.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IGeometryService, NtsGeometryService>();
builder.Services.AddSingleton<IWorldRepository, InMemoryWorldRepository>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<UiState>();
builder.Services.AddHostedService<GameTickService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/debug/state", (SanyTotalWar.Application.GameEngine engine) => new
{
    version = engine.W.Version,
    now = engine.W.GameNow,
    territories = engine.W.Territories.Count,
    activeWars = engine.W.Wars.Count(w => !w.Ended),
    tracks = engine.W.RecentTracks.Count,
    journal = engine.W.Journal.Count,
    lastJournal = engine.W.Journal.TakeLast(3).Select(j => j.Message),
});

app.Run();
