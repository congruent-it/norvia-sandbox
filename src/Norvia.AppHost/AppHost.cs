using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.Azure;
using Azure.Provisioning.ServiceBus;
using Norvia.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var seedFile = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "seed", "norvia.seed.json"));

// Locally SQL Server runs in a container. In Azure it is Azure SQL, and the database gets Aspire's default SKU:
// serverless (GP_S_Gen5_2) on the free offer, which pauses rather than bills when the free limit is reached.
var sql = builder.AddAzureSqlServer("sql").RunAsContainer();
var tmsDatabase = sql.AddDatabase("tms");
// The Partner portal's own records of what each Persona sent Norvia. It never reads the TMS's database (ADR 0002).
var partnerPortalDatabase = sql.AddDatabase("partner-portal");

// The estate Bicep owns Service Bus in Azure (ADR 0004); locally the emulator stands in for it.
var messaging = builder.AddAzureServiceBus("messaging");

// Migrations are a deployment step, not something the TMS does at startup (coding guidelines §5). Locally the TMS
// waits for them; in Azure they are a Container Apps job that azd's postdeploy hook starts.
var tmsMigrations = builder.AddProject<Projects.Norvia_Tms_Migrations>("tms-migrations")
    .WithReference(tmsDatabase).WaitFor(tmsDatabase)
    .PublishAsAzureContainerAppJob();

var tmsApi = builder.AddProject<Projects.Norvia_Tms_Api>("tms-api")
    .WithReference(tmsDatabase).WaitForCompletion(tmsMigrations)
    .WithReference(messaging).WaitFor(messaging)
    .WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataSender)
    .WithHttpHealthCheck("/health");

// The TMS UI reads the TMS database through the query side.
var tmsWeb = builder.AddProject<Projects.Norvia_Tms_Web>("tms-web")
    .WithReference(tmsDatabase).WaitForCompletion(tmsMigrations)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "TMS UI");

// The Partner portal plays every Shipper and Carrier as a Persona, and reaches Norvia only through the Norvia Partner
// API (ADR 0001). Its migrations are a deployment step too.
var partnerPortalMigrations = builder.AddProject<Projects.Norvia_PartnerPortal_Migrations>("partner-portal-migrations")
    .WithReference(partnerPortalDatabase).WaitFor(partnerPortalDatabase)
    .PublishAsAzureContainerAppJob();

var partnerPortal = builder.AddProject<Projects.Norvia_PartnerPortal_Web>("partner-portal-web")
    .WithReference(partnerPortalDatabase).WaitForCompletion(partnerPortalMigrations)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Partner portal");

// Locally the TMS and the Partner portal read the repo's seed file; in Azure the copy each image carries, next to the
// app (the image's working directory).
var seedPath = builder.ExecutionContext.IsPublishMode ? Path.Combine("seed", "norvia.seed.json") : seedFile;
foreach (var readerOfTheSeedFile in new[] { tmsApi, tmsWeb, partnerPortal })
{
    readerOfTheSeedFile.WithEnvironment("Seed__Path", seedPath);
}

var personas = SeedPersona.ReadAll(seedFile);

if (builder.ExecutionContext.IsPublishMode)
{
    var estate = new Estate(builder);

    // Aspire owns the Container Apps environment, which logs to the estate's workspace so its retention is the
    // estate's too (integrationRetentionDays).
    builder.AddAzureContainerAppEnvironment("apps")
        .WithAzureLogAnalyticsWorkspace(builder.AddAzureLogAnalyticsWorkspace("logs").PublishAsExisting(estate.LogAnalyticsWorkspace, null));

    messaging.PublishAsExisting(estate.ServiceBusNamespace, null);

    var insights = builder.AddAzureApplicationInsights("insights").PublishAsExisting(estate.ApplicationInsights, null);
    foreach (var project in new[] { tmsMigrations, tmsApi, tmsWeb, partnerPortalMigrations, partnerPortal })
    {
        project.WithReference(insights);
    }

    // In Azure the TMS trusts Entra only, and only the intake Logic App's managed identity may submit (ADR 0005).
    // The intake Logic App runs outside the Container Apps environment, so the API is reachable from outside it.
    tmsApi.WithExternalHttpEndpoints()
        .WithEnvironment("Authentication__Schemes__Bearer__Authority", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", estate.TmsApiClientId)
        .WithEnvironment("IntakeCallers__ClientIdsByChannel__ShipperApi__0", estate.IntakeClientId);

    // In Azure the Partner portal books through API Management as each Persona: it runs as the identity every Persona's
    // client app trusts, so it gets their tokens without a secret (ADR 0005), and reads their subscription keys from
    // the estate's Key Vault.
    var partnerPortalIdentity = builder.AddAzureUserAssignedIdentity("partner-portal-identity")
        .PublishAsExisting(estate.PartnerPortalIdentity, null);
    var partnerPortalSecrets = builder.AddAzureKeyVault("partner-portal-secrets")
        .PublishAsExisting(estate.PartnerPortalKeyVault, null);
    partnerPortal.WithAzureUserAssignedIdentity(partnerPortalIdentity)
        .WithEnvironment("PartnerApi__BaseAddress", estate.PartnerApiUrl)
        .WithEnvironment("PartnerApi__PersonaClientIds", estate.PersonaClientIds)
        .WithEnvironment("PartnerApi__Entra__TenantId", estate.EntraTenantId)
        .WithEnvironment("PartnerApi__Entra__Scope", ReferenceExpression.Create($"{estate.PartnerApiClientId}/.default"))
        .WithEnvironment("PartnerApi__Entra__ManagedIdentityClientId", estate.PartnerPortalClientId);
    foreach (var persona in personas)
    {
        partnerPortal.WithEnvironment($"PartnerApi__SubscriptionKeys__{persona.Id}", partnerPortalSecrets.Resource.GetSecret($"subscription-key-{persona.Id}"));
    }
}
else
{
    // The emulator won't start a topic without a subscription. Monitoring is the consumer P1 names for its events.
    messaging.RunAsEmulator(emulator => emulator.WithConfiguration(ResubmitAwareFilters.Apply))
        .AddServiceBusTopic("transport-order-events")
        .AddServiceBusSubscription("monitoring");

    // Locally the TMS trusts tokens signed with this key (see LocalTokens).
    var localTokenSigningKey = builder.AddParameter(
        LocalTokens.SigningKeyParameter,
        new GenerateParameterDefault { MinLength = 64, Special = false },
        secret: true,
        persist: true);

    tmsApi
        .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", LocalTokens.Issuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", LocalTokens.TmsAudience)
        .WithEnvironment("Authentication__Schemes__Bearer__SigningKeys__0__Issuer", LocalTokens.Issuer)
        .WithEnvironment("IntakeCallers__ClientIdsByChannel__ShipperApi__0", LocalTokens.IntakeLogicAppClientId)
        .WithUrlForEndpoint("http", url =>
        {
            url.Url = "/scalar";
            url.DisplayText = "API reference (Scalar)";
        });

    // Locally a stand-in plays API Management and the intake Logic App (ADR 0023). The AppHost gives each Persona what
    // the estate gives it in Azure: a client ID, a subscription key for the run, and the submit role if it is a Shipper.
    var partnerApi = builder.AddProject<Projects.Norvia_Integration_LocalPartnerApi>("partner-api")
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithHttpHealthCheck("/health")
        .WithEnvironment("PartnerApi__Tokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("PartnerApi__Tokens__Audience", LocalTokens.PartnerApiAudience)
        .WithEnvironment("Tms__BaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("Tms__Tokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("Tms__Tokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("Tms__Tokens__ClientId", LocalTokens.IntakeLogicAppClientId);
    partnerPortal.WaitFor(partnerApi)
        .WithEnvironment("PartnerApi__BaseAddress", ReferenceExpression.Create($"{partnerApi.GetEndpoint("http")}/partner/v1"))
        .WithEnvironment("PartnerApi__PersonaClientIds", string.Join(';', personas.Select(persona => $"{persona.Id}={LocalTokens.PersonaClientId(persona.Id)}")))
        .WithEnvironment("PartnerApi__LocalTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("PartnerApi__LocalTokens__Audience", LocalTokens.PartnerApiAudience);
    foreach (var persona in personas)
    {
        var subscriptionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        partnerApi.WithEnvironment($"PartnerApi__SubscriptionKeys__{persona.Id}", subscriptionKey);
        partnerPortal.WithEnvironment($"PartnerApi__SubscriptionKeys__{persona.Id}", subscriptionKey);
        if (persona.IsShipper)
        {
            partnerApi.WithEnvironment($"PartnerApi__ShippersByClientId__{LocalTokens.PersonaClientId(persona.Id)}", persona.PartyId);
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__0", LocalTokens.SubmitRole);
        }
    }

    // All three sign or check tokens with the same local key, as JWT bearer configuration takes it: base64.
    foreach (var (holder, name) in new[]
    {
        (tmsApi.Resource as IResourceWithEnvironment, "Authentication__Schemes__Bearer__SigningKeys__0__Value"),
        (partnerApi.Resource, "PartnerApi__Tokens__SigningKey"),
        (partnerApi.Resource, "Tms__Tokens__SigningKey"),
        (partnerPortal.Resource, "PartnerApi__LocalTokens__SigningKey"),
    })
    {
        builder.CreateResourceBuilder(holder).WithEnvironment(async context =>
        {
            var key = await localTokenSigningKey.Resource.GetValueAsync(context.CancellationToken);
            context.EnvironmentVariables[name] = Convert.ToBase64String(Encoding.UTF8.GetBytes(key!));
        });
    }
}

builder.Build().Run();
