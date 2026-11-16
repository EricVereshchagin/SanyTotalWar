using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SanyTotalWar.Application;
using SanyTotalWar.Application.Abstractions;
using SanyTotalWar.Infrastructure.Geo;
using SanyTotalWar.Infrastructure.Persistence;
using SanyTotalWar.Wasm;
using SanyTotalWar.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<IGeometryService, NtsGeometryService>();
builder.Services.AddSingleton<IWorldRepository, InMemoryWorldRepository>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<UiState>();

await builder.Build().RunAsync();
