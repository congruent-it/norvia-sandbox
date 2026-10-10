using Norvia.DriverApp.Infrastructure;

// Applies the Driver app database's EF Core migrations, then exits. It runs as a deployment step: before the Driver app
// starts locally, and as a Container Apps job in Azure (coding guidelines §5).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddDriverAppDatabaseMigrations();

using var host = builder.Build();
await host.Services.MigrateDriverAppDatabaseAsync();
