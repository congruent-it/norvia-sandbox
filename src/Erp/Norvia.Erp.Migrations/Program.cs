using Norvia.Erp.Infrastructure;

// Applies the ERP database's EF Core migrations, then exits. It runs as a deployment step: before the ERP starts
// locally, and as a Container Apps job in Azure (coding guidelines §5).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddErpDatabaseMigrations();

using var host = builder.Build();
await host.Services.MigrateErpDatabaseAsync();
