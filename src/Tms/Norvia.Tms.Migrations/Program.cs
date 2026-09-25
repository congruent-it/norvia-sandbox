using Norvia.Tms.Infrastructure;

// Applies the TMS database's EF Core migrations, then exits. It runs as a deployment step: before the TMS starts
// locally, and as a Container Apps job in Azure (coding guidelines §5).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddTmsDatabaseMigrations();

using var host = builder.Build();
await host.Services.MigrateTmsDatabaseAsync();
