using Norvia.Simulator.Infrastructure;

// Applies the Simulator database's EF Core migrations, then exits. It runs as a deployment step: before the Simulator
// starts locally (coding guidelines §5).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddSimulatorDatabaseMigrations();

using var host = builder.Build();
await host.Services.MigrateSimulatorDatabaseAsync();
