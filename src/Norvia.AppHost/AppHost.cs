using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.Azure;
using Azure.Provisioning.ServiceBus;
using Azure.Provisioning.Sql;
using Norvia.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var seedFile = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "seed", "norvia.seed.json"));

// Locally SQL Server runs in a container. In Azure it is Azure SQL, and each database gets Aspire's default SKU:
// serverless (GP_S_Gen5_2) on the free offer. Once a month's free allowance is used, a database bills for what it uses
// rather than pausing, as the free offer would by default: a paused database fails the migrations and the demo until
// the month ends.
var sql = builder.AddAzureSqlServer("sql").RunAsContainer()
    .ConfigureInfrastructure(infrastructure =>
    {
        foreach (var database in infrastructure.GetProvisionableResources().OfType<SqlDatabase>())
        {
            database.FreeLimitExhaustionBehavior = FreeLimitExhaustionBehavior.BillOverUsage;
        }
    });
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

// The Integration console, for the Integration operator. It has no database: it reads what the estate records (ADR 0017).
var integrationConsole = builder.AddProject<Projects.Norvia_IntegrationConsole_Web>("integration-console-web")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Integration console");

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
    foreach (var project in new[] { tmsMigrations, tmsApi, tmsWeb, partnerPortalMigrations, partnerPortal, integrationConsole })
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

    // The TMS UI and the Partner portal have no login of their own, so in Azure they open to the presenter's browser
    // behind an Entra sign-in that only the presenter passes (ADR 0024). The apps don't know it is there.
    foreach (var demoUi in new[] { tmsWeb, partnerPortal })
    {
        demoUi.WithExternalHttpEndpoints()
            .PublishAsAzureContainerApp((infrastructure, app) =>
                infrastructure.Add(EntraSignIn.For(infrastructure, app, estate.DemoUisClientId, estate.EntraIssuer)));
    }

    // The Integration console opens to the operators' browsers. Easy Auth signs them in to the console's own app
    // registration, whose app roles the console checks (ADR 0026). It then reads the estate as its own identity, never
    // with the operator's token (ADR 0017).
    var integrationConsoleIdentity = builder.AddAzureUserAssignedIdentity("integration-console-identity")
        .PublishAsExisting(estate.IntegrationConsoleIdentity, null);
    integrationConsole.WithExternalHttpEndpoints()
        .WithAzureUserAssignedIdentity(integrationConsoleIdentity)
        .WithEnvironment("Estate__LogAnalyticsWorkspaceId", estate.LogAnalyticsWorkspaceId)
        .WithEnvironment("Estate__IntegrationRetentionDays", estate.IntegrationRetentionDays)
        .WithEnvironment("Estate__ResourceGroupId", estate.ResourceGroupId)
        .PublishAsAzureContainerApp((infrastructure, app) =>
            infrastructure.Add(EntraSignIn.For(infrastructure, app, estate.IntegrationConsoleClientId, estate.EntraIssuer)));

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

    // Locally there is no Easy Auth, so the console signs the developer in as an Integration operator with both app
    // roles, as the estate assigns the presenter. It reads Azure with the developer's own sign-in.
    // It searches a deployed estate's workspace once the developer gives it the parameters azd gives it in Azure, as
    // user secrets (Parameters:estate-log-analytics-workspace-id and Parameters:estate-integration-retention-days), and
    // opens its Logic App runs given Parameters:estate-resource-group-id. Logic Apps exist only in Azure.
    // Without a workspace it still starts, and search says it can't be run; without the resource group, a run does.
    var estateWorkspaceId = builder.Configuration["Parameters:estate-log-analytics-workspace-id"];
    var estateRetentionDays = builder.Configuration["Parameters:estate-integration-retention-days"];
    if (!string.IsNullOrEmpty(estateWorkspaceId) && string.IsNullOrEmpty(estateRetentionDays))
    {
        // Guessing would cap searches at a period the estate doesn't keep.
        throw new InvalidOperationException(
            "The Integration console has an estate's workspace but not its retention: set Parameters:estate-integration-retention-days " +
            "to the estate's integrationRetentionDays (AZURE_ESTATE_INTEGRATION_RETENTION_DAYS in azd env get-values).");
    }

    integrationConsole
        .WithEnvironment("LocalOperator__Name", $"{Environment.UserName} (local)")
        .WithEnvironment("LocalOperator__Roles__0", "IntegrationConsole.Read")
        .WithEnvironment("LocalOperator__Roles__1", "IntegrationConsole.Resubmit")
        .WithEnvironment("Estate__LogAnalyticsWorkspaceId", estateWorkspaceId ?? string.Empty)
        .WithEnvironment("Estate__ResourceGroupId", builder.Configuration["Parameters:estate-resource-group-id"] ?? string.Empty)
        // Without an estate no search reaches Azure, so the retention is only a placeholder the console starts with.
        .WithEnvironment("Estate__IntegrationRetentionDays", estateRetentionDays ?? "90");

    // Without an estate, the console searches what the local apps traced: the telemetry the Aspire dashboard holds, read
    // through its telemetry API (ADR 0027). The dashboard listens on the launch profile's URL, and its API takes the key
    // the AppHost generates for it. Nothing local is sent to an estate, so local and Azure trails never mix.
    if (string.IsNullOrEmpty(estateWorkspaceId)
        && DashboardUrl(builder.Configuration["ASPNETCORE_URLS"]) is { } dashboardUrl)
    {
        integrationConsole
            .WithEnvironment("LocalTelemetry__DashboardUrl", dashboardUrl)
            .WithEnvironment("LocalTelemetry__ApiKey", builder.Configuration["AppHost:DashboardApiKey"] ?? string.Empty);
    }

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

// The dashboard's address from the launch profile's ASPNETCORE_URLS: its http one when it has one, which the console
// reaches without the development certificate.
static string? DashboardUrl(string? urls)
{
    var all = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    return all.FirstOrDefault(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault();
}
