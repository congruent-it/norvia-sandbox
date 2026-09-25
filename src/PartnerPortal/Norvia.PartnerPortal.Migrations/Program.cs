using Norvia.PartnerPortal.Infrastructure;

// Applies the Partner portal database's EF Core migrations, then exits. It runs as a deployment step: before the portal
// starts locally, and as a Container Apps job in Azure (coding guidelines §5).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddPartnerPortalDatabaseMigrations();

using var host = builder.Build();
await host.Services.MigratePartnerPortalDatabaseAsync();
