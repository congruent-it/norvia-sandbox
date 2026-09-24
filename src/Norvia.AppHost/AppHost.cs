var builder = DistributedApplication.CreateBuilder(args);

var seedFile = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "seed", "norvia.seed.json"));

var sql = builder.AddSqlServer("sql");
var tmsDatabase = sql.AddDatabase("tms");

// The estate Bicep owns Service Bus in Azure (ADR 0004); locally the emulator stands in for it.
var messaging = builder.AddAzureServiceBus("messaging").RunAsEmulator();
messaging.AddServiceBusTopic("transport-order-events");

builder.AddProject<Projects.Norvia_Tms_Api>("tms-api")
    .WithReference(tmsDatabase).WaitFor(tmsDatabase)
    .WithReference(messaging).WaitFor(messaging)
    .WithEnvironment("Seed__Path", seedFile)
    .WithEnvironment("Database__MigrateOnStartup", "true")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
