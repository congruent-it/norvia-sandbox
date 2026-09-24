using System.Text;
using Norvia.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var seedFile = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "seed", "norvia.seed.json"));

var sql = builder.AddSqlServer("sql");
var tmsDatabase = sql.AddDatabase("tms");

// The estate Bicep owns Service Bus in Azure (ADR 0004); locally the emulator stands in for it.
var messaging = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator(emulator => emulator.WithConfiguration(ResubmitAwareFilters.Apply));

// The emulator won't start a topic without a subscription. Monitoring is the consumer P1 names for its events.
messaging.AddServiceBusTopic("transport-order-events")
    .AddServiceBusSubscription("monitoring");

// In Azure the TMS trusts Entra; locally it trusts tokens signed with this key (see LocalTokens).
var localTokenSigningKey = builder.AddParameter(
    LocalTokens.SigningKeyParameter,
    new GenerateParameterDefault { MinLength = 64, Special = false },
    secret: true,
    persist: true);

builder.AddProject<Projects.Norvia_Tms_Api>("tms-api")
    .WithReference(tmsDatabase).WaitFor(tmsDatabase)
    .WithReference(messaging).WaitFor(messaging)
    .WithEnvironment("Seed__Path", seedFile)
    .WithEnvironment("Database__MigrateOnStartup", "true")
    .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", LocalTokens.Issuer)
    .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", LocalTokens.TmsAudience)
    .WithEnvironment("Authentication__Schemes__Bearer__SigningKeys__0__Issuer", LocalTokens.Issuer)
    .WithEnvironment($"IntakeCallers__ChannelByClientId__{LocalTokens.IntakeLogicAppClientId}", "ShipperApi")
    .WithEnvironment(async context =>
    {
        // JWT bearer configuration takes a signing key as base64.
        var key = await localTokenSigningKey.Resource.GetValueAsync(context.CancellationToken);
        context.EnvironmentVariables["Authentication__Schemes__Bearer__SigningKeys__0__Value"] =
            Convert.ToBase64String(Encoding.UTF8.GetBytes(key!));
    })
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/scalar";
        url.DisplayText = "API reference (Scalar)";
    });

builder.Build().Run();
