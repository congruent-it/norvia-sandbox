using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.Azure;
using Azure.Provisioning.ServiceBus;
using Azure.Provisioning.Sql;
using Azure.Provisioning.Storage;
using Norvia.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var seedFile = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "seed", "norvia.seed.json"));

// Locally SQL Server runs in a container. In Azure it is Azure SQL, and each database gets Aspire's default SKU:
// serverless (GP_S_Gen5_2) on the free offer. Once a month's free allowance is used, a database bills for what it uses
// rather than pausing, as the free offer would by default: a paused database fails the migrations and the demo until
// the month ends.
// Locally DbGate runs beside it, to inspect the databases (it is part of the local run only).
var sql = builder.AddAzureSqlServer("sql").RunAsContainer(container => container.WithDbGate())
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
// The Driver app's own records of the Trips it was sent. It never reads the TMS's database (ADR 0002).
var driverAppDatabase = sql.AddDatabase("driver-app");

// The estate Bicep owns Service Bus in Azure (ADR 0004); locally the emulator stands in for it.
var messaging = builder.AddAzureServiceBus("messaging");

// The TMS keeps Proof of delivery files in a blob container of its own (ADR 0002). Locally Azurite stands in for the storage
// account. In Azure a lifecycle rule moves the files to the cool tier after 30 days; they are never deleted.
var storage = builder.AddAzureStorage("storage").RunAsEmulator()
    .ConfigureInfrastructure(infrastructure => ProofOfDeliveryStorage.AddLifecycleRule(infrastructure));
var blobs = storage.AddBlobs("blobs");
storage.AddBlobContainer(ProofOfDeliveryStorage.ContainerName);

// Migrations are a deployment step, not something the TMS does at startup (coding guidelines §5). Locally the TMS
// waits for them; in Azure they are a Container Apps job that azd's postdeploy hook starts.
var tmsMigrations = builder.AddProject<Projects.Norvia_Tms_Migrations>("tms-migrations")
    .WithReference(tmsDatabase).WaitFor(tmsDatabase)
    .PublishAsAzureContainerAppJob();

var tmsApi = builder.AddProject<Projects.Norvia_Tms_Api>("tms-api")
    .WithReference(tmsDatabase).WaitForCompletion(tmsMigrations)
    .WithReference(messaging).WaitFor(messaging)
    .WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataSender)
    .WithReference(blobs).WaitFor(blobs)
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataContributor)
    .WithHttpHealthCheck("/health");

// The TMS UI reads the TMS database through the query side, and changes things only through the TMS API (ADR 0028).
var tmsWeb = builder.AddProject<Projects.Norvia_Tms_Web>("tms-web")
    .WithReference(tmsDatabase).WaitForCompletion(tmsMigrations)
    .WithReference(tmsApi).WaitFor(tmsApi)
    .WithReference(blobs).WaitFor(blobs)
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataReader)
    .WithEnvironment("TmsApi__BaseAddress", tmsApi.GetEndpoint("http"))
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

// The Driver app is its own application and reaches the TMS only through the Trip dispatch Logic App (ADR 0001).
var driverAppMigrations = builder.AddProject<Projects.Norvia_DriverApp_Migrations>("driver-app-migrations")
    .WithReference(driverAppDatabase).WaitFor(driverAppDatabase)
    .PublishAsAzureContainerAppJob();

// Its outbox publishes what the Driver reports to the topic for Driver app events (P12), and it takes the Driver reports Logic App's outcome reports.
var driverAppApi = builder.AddProject<Projects.Norvia_DriverApp_Api>("driver-app-api")
    .WithReference(driverAppDatabase).WaitForCompletion(driverAppMigrations)
    .WithReference(messaging).WaitFor(messaging)
    .WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataSender)
    .WithHttpHealthCheck("/health");

// The Partner portal's presenter panel switches the Driver app's "unavailable" fault through its API (P11).
partnerPortal.WithEnvironment("DriverApp__Address", driverAppApi.GetEndpoint("http"));

// The Driver's screens: sign in as a seeded Driver, then a read-only list of that Driver's Trips. Locally a stand-in plays
// Entra ID's sign-in (ADR 0037); in Azure Easy Auth signs Drivers in.
var driverAppWeb = builder.AddProject<Projects.Norvia_DriverApp_Web>("driver-app-web")
    .WithReference(driverAppDatabase).WaitForCompletion(driverAppMigrations)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Driver app");

// The Integration console, for the Integration operator. It has no database: it reads what the estate records (ADR 0017).
var integrationConsole = builder.AddProject<Projects.Norvia_IntegrationConsole_Web>("integration-console-web")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Integration console");

// Locally the TMS and the Partner portal read the repo's seed file; in Azure the copy each image carries, next to the
// app (the image's working directory).
var seedPath = builder.ExecutionContext.IsPublishMode ? Path.Combine("seed", "norvia.seed.json") : seedFile;
foreach (var readerOfTheSeedFile in new[] { tmsApi, tmsWeb, partnerPortal, driverAppApi, driverAppWeb })
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
    foreach (var project in new[] { tmsMigrations, tmsApi, tmsWeb, partnerPortalMigrations, partnerPortal, driverAppMigrations, driverAppApi, driverAppWeb, integrationConsole })
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

    // The TMS UI calls the TMS API as its own identity, which the estate grants the TMS API's manage role; it is the
    // one caller that may re-rate.
    var tmsUiIdentity = builder.AddAzureUserAssignedIdentity("tms-ui-identity")
        .PublishAsExisting(estate.TmsUiIdentity, null);
    tmsWeb.WithAzureUserAssignedIdentity(tmsUiIdentity)
        .WithEnvironment("TmsApi__Entra__Scope", ReferenceExpression.Create($"{estate.TmsApiClientId}/.default"))
        .WithEnvironment("TmsApi__Entra__ManagedIdentityClientId", estate.TmsUiClientId);

    // The TMS UI and the Partner portal have no login of their own, so in Azure they open to the presenter's browser
    // behind an Entra sign-in that only the presenter passes (ADR 0024). The apps don't know it is there.
    // The exceptions are each Carrier Persona's booking and cancellation endpoints and each Shipper Persona's notification
    // endpoint: the Logic Apps that call them are no browsers, and the endpoints ask them for the party's own credential.
    var personaEndpoints = personas.Select(persona => persona.EndpointPath)
        .Concat(personas.Select(persona => persona.CancellationPath).OfType<string>())
        .ToList();
    foreach (var (demoUi, pathsOutsideTheGate) in new[] { (tmsWeb, []), (partnerPortal, personaEndpoints) })
    {
        demoUi.WithExternalHttpEndpoints()
            .PublishAsAzureContainerApp((infrastructure, app) =>
                infrastructure.Add(EntraSignIn.For(infrastructure, app, estate.DemoUisClientId, estate.EntraIssuer, pathsOutsideTheGate)));
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

    // The Driver app's Trip endpoint asks its caller, the P11 Trip dispatch Logic App, for a credential the estate keeps in a
    // vault of its own as driver-app-endpoint-key. The Logic App runs outside the Container Apps environment, so the API is
    // reachable from outside it.
    var driverAppIdentity = builder.AddAzureUserAssignedIdentity("driver-app-identity")
        .PublishAsExisting(estate.DriverAppIdentity, null);
    var driverAppSecrets = builder.AddAzureKeyVault("driver-app-secrets")
        .PublishAsExisting(estate.DriverAppKeyVault, null);
    // Its outcome endpoint trusts Entra only, and only the Driver reports Logic App's managed identity may report (ADR 0005).
    driverAppApi.WithExternalHttpEndpoints()
        .WithAzureUserAssignedIdentity(driverAppIdentity)
        .WithEnvironment("DriverEndpoint__ApiKey", driverAppSecrets.Resource.GetSecret("driver-app-endpoint-key"))
        .WithEnvironment("Authentication__Schemes__Bearer__Authority", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", estate.DriverAppApiClientId);

    // Drivers sign in to the Driver app with Entra ID. Easy Auth passes the Driver's token on, and the Driver ID is the app role
    // in it (ADR 0037). The presenter panel shows the Driver test users the estate made.
    driverAppWeb.WithExternalHttpEndpoints()
        .PublishAsAzureContainerApp((infrastructure, app) =>
            infrastructure.Add(EntraSignIn.For(infrastructure, app, estate.DriverAppRegistrationClientId, estate.EntraIssuer)));
    partnerPortal.WithEnvironment("DriverSignIns__Domain", estate.DriverSignInDomain)
        .WithEnvironment("DriverSignIns__Password", estate.DriverSignInPassword);

    // Each Carrier Persona's booking endpoint asks its caller, the P4 booking Logic App, for the credential the estate
    // keeps in the same vault as carrier-endpoint-key-<Carrier ID>.
    foreach (var carrier in personas.Where(persona => !persona.IsShipper))
    {
        partnerPortal.WithEnvironment($"CarrierEndpoint__ApiKeys__{carrier.PartyId}", partnerPortalSecrets.Resource.GetSecret($"carrier-endpoint-key-{carrier.PartyId}"));
    }

    // Likewise each Shipper Persona's notification endpoint, for the Shipper notification Logic App (P7).
    foreach (var shipper in personas.Where(persona => persona.IsShipper))
    {
        partnerPortal.WithEnvironment($"ShipperEndpoint__ApiKeys__{shipper.PartyId}", partnerPortalSecrets.Resource.GetSecret($"shipper-endpoint-key-{shipper.PartyId}"));
    }
}
else
{
    // The emulator won't start a topic without a subscription. Monitoring is the consumer P1 and P3 name for their events.
    messaging.RunAsEmulator(emulator => emulator.WithConfiguration(ResubmitAwareFilters.Apply));
    messaging.AddServiceBusTopic("transport-order-events")
        .AddServiceBusSubscription("monitoring");
    // Resource names are unique across the AppHost, so this monitoring subscription's resource is named for its topic.
    var shipmentEvents = messaging.AddServiceBusTopic("shipment-events");
    shipmentEvents.AddServiceBusSubscription("shipment-events-monitoring", subscriptionName: "monitoring");
    // The P4 booking Logic App's subscription: Carrier Shipments only (see ResubmitAwareFilters).
    shipmentEvents.AddServiceBusSubscription("carrier-booking");
    // The P7 Shipper notification Logic App's subscription: ShipperNotificationRequested only (see ResubmitAwareFilters).
    shipmentEvents.AddServiceBusSubscription("shipper-notification");
    // The P7 Carrier cancellation Logic App's subscription: CarrierBookingCancelled and CarrierBookingWithdrawn only (see ResubmitAwareFilters).
    shipmentEvents.AddServiceBusSubscription("carrier-cancellation");
    // The Trip events topic (P11). Monitoring is its first consumer, then the Trip dispatch Logic App with a subscription of its
    // own: TripDispatched only (see ResubmitAwareFilters).
    var tripEvents = messaging.AddServiceBusTopic("trip-events");
    tripEvents.AddServiceBusSubscription("trip-events-monitoring", subscriptionName: "monitoring");
    tripEvents.AddServiceBusSubscription("trip-dispatch");
    // The topic for Driver app events (P12). The Driver reports Logic App is the only consumer, with a subscription of its own that uses sessions
    // keyed by Trip number, so one Trip's reports are read one at a time and in order: TripStartReported and StopCompletedReported only (see ResubmitAwareFilters).
    messaging.AddServiceBusTopic("driver-app-events")
        .AddServiceBusSubscription("driver-reports")
        .WithProperties(subscription => subscription.RequiresSession = true);
    // The tracking queue API Management puts a Carrier's tracking message on and the Carrier-tracking Logic App reads
    // (ADR 0008). A message the TMS refuses is dead-lettered; one it can't be given to is delivered again up to the
    // maximum delivery count, then dead-lettered by Service Bus itself. The estate Bicep sets the same count.
    messaging.AddServiceBusQueue("carrier-tracking").WithProperties(queue => queue.MaxDeliveryCount = 10);
    // The Proof of delivery queue holds only the reference to a blob in the inbound container, which API Management stores a
    // Carrier's Proof of delivery in (ADR 0009). Azurite holds the container here; the estate Bicep makes it in Azure.
    messaging.AddServiceBusQueue("carrier-proof-of-delivery").WithProperties(queue => queue.MaxDeliveryCount = 10);
    storage.AddBlobContainer("inbound-proofs-of-delivery");

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

    // Locally there is no Entra ID either, so the Driver app's sign-in page stands in for it and lists the seeded Drivers (ADR 0037).
    driverAppWeb.WithEnvironment("LocalSignIn__Enabled", "true");

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

    // Locally the TMS UI signs its token for the TMS API itself, as Entra would issue it to its managed identity.
    tmsWeb
        .WithEnvironment("TmsApi__LocalTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("TmsApi__LocalTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("TmsApi__LocalTokens__ClientId", LocalTokens.TmsUiClientId);

    // Locally a stand-in plays API Management and the intake and Carrier-answer Logic Apps (ADR 0023), and puts a Carrier's tracking message on the tracking queue. The AppHost gives
    // each Persona what the estate gives it in Azure: a client ID, a subscription key for the run, and the submit and tracking read roles if it
    // is a Shipper or the respond role if it is a Carrier.
    var partnerApi = builder.AddProject<Projects.Norvia_Integration_LocalPartnerApi>("partner-api")
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(blobs).WaitFor(blobs)
        .WithHttpHealthCheck("/health")
        .WithEnvironment("PartnerApi__Tokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("PartnerApi__Tokens__Audience", LocalTokens.PartnerApiAudience)
        .WithEnvironment("Tms__BaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("Tms__Tokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("Tms__Tokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("Tms__Tokens__ClientId", LocalTokens.IntakeLogicAppClientId)
        .WithEnvironment("Tms__AnswerClientId", LocalTokens.AnswerLogicAppClientId)
        .WithEnvironment("Tms__ShipperTrackingClientId", LocalTokens.ApiManagementClientId);
    // Locally a worker plays the P4 booking Logic App (ADR 0030). It reads the booking subscription, sends each Carrier
    // Persona's booking request to the Persona's endpoint in the Partner portal with a credential for the run, and
    // reports the outcome to the TMS as the identity holding the report role.
    var bookingWorker = builder.AddProject<Projects.Norvia_Integration_LocalBookingWorker>("booking-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithEnvironment("Booking__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("Booking__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("Booking__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("Booking__TmsTokens__ClientId", LocalTokens.BookingLogicAppClientId);
    // Locally a worker plays the P7 Shipper notification Logic App as well (ADR 0033). It reads its subscription, sends each
    // Shipper Persona's notification to the Persona's endpoint in the Partner portal with a credential for the run, and
    // reports the outcome to the TMS as the identity holding the report role.
    var shipperNotificationWorker = builder.AddProject<Projects.Norvia_Integration_LocalShipperNotificationWorker>("shipper-notification-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithEnvironment("ShipperNotification__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("ShipperNotification__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("ShipperNotification__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("ShipperNotification__TmsTokens__ClientId", LocalTokens.ShipperNotificationLogicAppClientId);
    // Locally a worker plays the P7 Carrier cancellation Logic App too (ADR 0034). It reads its subscription, sends each Carrier
    // Persona's cancellation or withdrawal to the Persona's cancellation endpoint in the Partner portal with the credential for the
    // run, and reports it told or not delivered to the TMS as the identity holding the Carrier booking report role.
    var carrierCancellationWorker = builder.AddProject<Projects.Norvia_Integration_LocalCarrierCancellationWorker>("carrier-cancellation-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithEnvironment("CarrierCancellation__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("CarrierCancellation__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("CarrierCancellation__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("CarrierCancellation__TmsTokens__ClientId", LocalTokens.CarrierCancellationLogicAppClientId);
    // Locally a worker plays the P11 Trip dispatch Logic App as well. It reads its subscription, sends each dispatched Trip to the
    // Driver app's Trip endpoint with the credential for the run, and reports the version delivered or not delivered to the TMS as
    // the identity holding the Trip delivery report role.
    var driverEndpointKey = builder.AddParameter(
        "driver-app-endpoint-key",
        new GenerateParameterDefault { MinLength = 32, Special = false },
        secret: true,
        persist: true);
    driverAppApi.WithEnvironment("DriverEndpoint__ApiKey", driverEndpointKey)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", LocalTokens.Issuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", LocalTokens.DriverAppAudience)
        .WithEnvironment("Authentication__Schemes__Bearer__SigningKeys__0__Issuer", LocalTokens.Issuer);
    var tripDispatchWorker = builder.AddProject<Projects.Norvia_Integration_LocalTripDispatchWorker>("trip-dispatch-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WaitFor(driverAppApi)
        .WithEnvironment("TripDispatch__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("TripDispatch__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("TripDispatch__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("TripDispatch__TmsTokens__ClientId", LocalTokens.TripDispatchLogicAppClientId)
        .WithEnvironment("TripDispatch__DriverApp__Address", ReferenceExpression.Create($"{driverAppApi.GetEndpoint("http")}/v1/trips"))
        .WithEnvironment("TripDispatch__DriverApp__ApiKey", driverEndpointKey);
    // Locally a worker plays the P12 Driver reports Logic App as well. It reads the Driver app events subscription one Trip at a time, calls the
    // TMS as the identity holding the Trip start role, and tells the Driver app what became of each report as the identity holding the outcome
    // role.
    var driverReportsWorker = builder.AddProject<Projects.Norvia_Integration_LocalDriverReportsWorker>("driver-reports-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WaitFor(driverAppApi)
        .WithEnvironment("DriverReports__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("DriverReports__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("DriverReports__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("DriverReports__TmsTokens__ClientId", LocalTokens.DriverReportsLogicAppClientId)
        .WithEnvironment("DriverReports__DriverApp__BaseAddress", driverAppApi.GetEndpoint("http"))
        .WithEnvironment("DriverReports__DriverApp__Tokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("DriverReports__DriverApp__Tokens__Audience", LocalTokens.DriverAppAudience)
        .WithEnvironment("DriverReports__DriverApp__Tokens__ClientId", LocalTokens.DriverReportsLogicAppClientId);
    // Locally a worker plays the Carrier-tracking Logic App too (ADR 0031). It reads the tracking queue API Management's
    // stand-in fills and records each Tracking event with the TMS as the identity holding the record role.
    var trackingWorker = builder.AddProject<Projects.Norvia_Integration_LocalTrackingWorker>("tracking-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithEnvironment("Tracking__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("Tracking__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("Tracking__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("Tracking__TmsTokens__ClientId", LocalTokens.TrackingLogicAppClientId);
    // Locally a worker plays the Carrier Proof of delivery Logic App as well (ADR 0032). It reads the Proof of delivery queue
    // the stand-in fills, fetches the blob the stand-in stored, and records the Proof of delivery with the TMS as the
    // identity holding the record role.
    var proofOfDeliveryWorker = builder.AddProject<Projects.Norvia_Integration_LocalProofOfDeliveryWorker>("proof-of-delivery-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(blobs).WaitFor(blobs)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithEnvironment("ProofOfDelivery__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("ProofOfDelivery__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("ProofOfDelivery__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("ProofOfDelivery__TmsTokens__ClientId", LocalTokens.ProofOfDeliveryLogicAppClientId);
    foreach (var persona in personas.Where(persona => !persona.IsShipper))
    {
        var carrierApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        bookingWorker
            .WithEnvironment($"Booking__Carriers__{persona.PartyId}__Address", ReferenceExpression.Create($"{partnerPortal.GetEndpoint("http")}{persona.EndpointPath}"))
            .WithEnvironment($"Booking__Carriers__{persona.PartyId}__ApiKey", carrierApiKey);
        carrierCancellationWorker
            .WithEnvironment($"CarrierCancellation__Carriers__{persona.PartyId}__Address", ReferenceExpression.Create($"{partnerPortal.GetEndpoint("http")}{persona.CancellationPath}"))
            .WithEnvironment($"CarrierCancellation__Carriers__{persona.PartyId}__ApiKey", carrierApiKey);
        // The Persona's booking and cancellation endpoints ask their callers for the same credential, as Key Vault holds it in Azure.
        partnerPortal.WithEnvironment($"CarrierEndpoint__ApiKeys__{persona.PartyId}", carrierApiKey);
    }

    // Each Shipper Persona's notification endpoint asks its caller, the P7 Shipper notification Logic App, for a credential
    // too, which the worker has for the run as Key Vault holds it in Azure.
    foreach (var persona in personas.Where(persona => persona.IsShipper))
    {
        var shipperApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        shipperNotificationWorker
            .WithEnvironment($"ShipperNotification__Shippers__{persona.PartyId}__Address", ReferenceExpression.Create($"{partnerPortal.GetEndpoint("http")}{persona.EndpointPath}"))
            .WithEnvironment($"ShipperNotification__Shippers__{persona.PartyId}__ApiKey", shipperApiKey);
        partnerPortal.WithEnvironment($"ShipperEndpoint__ApiKeys__{persona.PartyId}", shipperApiKey);
    }

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
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__1", LocalTokens.TrackingReadRole);
        }
        else
        {
            // A Carrier Persona answers the booking requests in its inbox, for the Carrier its client stands for.
            partnerApi.WithEnvironment($"PartnerApi__CarriersByClientId__{LocalTokens.PersonaClientId(persona.Id)}", persona.PartyId);
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__0", LocalTokens.RespondRole);
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__1", LocalTokens.TrackingSubmitRole);
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__2", LocalTokens.ProofsOfDeliverySubmitRole);
        }
    }

    // They all sign or check tokens with the same local key, as JWT bearer configuration takes it: base64.
    foreach (var (holder, name) in new[]
    {
        (tmsApi.Resource as IResourceWithEnvironment, "Authentication__Schemes__Bearer__SigningKeys__0__Value"),
        (tmsWeb.Resource, "TmsApi__LocalTokens__SigningKey"),
        (partnerApi.Resource, "PartnerApi__Tokens__SigningKey"),
        (partnerApi.Resource, "Tms__Tokens__SigningKey"),
        (bookingWorker.Resource, "Booking__TmsTokens__SigningKey"),
        (shipperNotificationWorker.Resource, "ShipperNotification__TmsTokens__SigningKey"),
        (carrierCancellationWorker.Resource, "CarrierCancellation__TmsTokens__SigningKey"),
        (tripDispatchWorker.Resource, "TripDispatch__TmsTokens__SigningKey"),
        (driverReportsWorker.Resource, "DriverReports__TmsTokens__SigningKey"),
        (driverReportsWorker.Resource, "DriverReports__DriverApp__Tokens__SigningKey"),
        (driverAppApi.Resource, "Authentication__Schemes__Bearer__SigningKeys__0__Value"),
        (trackingWorker.Resource, "Tracking__TmsTokens__SigningKey"),
        (proofOfDeliveryWorker.Resource, "ProofOfDelivery__TmsTokens__SigningKey"),
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
