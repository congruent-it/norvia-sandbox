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
// SQL Server takes the memory the machine has unless told otherwise, and a test run starts several AppHosts, so the container is held to 1 GB.
var sql = builder.AddAzureSqlServer("sql").RunAsContainer(container => container.WithDbGate().WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "1024"))
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
// The ERP's own database: master data, and from the slices after this one billable lines, Shipper invoices and Credit notes (ADR 0002).
var erpDatabase = sql.AddDatabase("erp");

// The estate Bicep owns Service Bus in Azure (ADR 0004); locally the emulator stands in for it.
var messaging = builder.AddAzureServiceBus("messaging");

// Locally the Service Bus emulator accepts only 10 concurrent connections per namespace (the Microsoft docs say it can be configured, but no
// setting of the emulator image honours that), and every app that sends or receives holds one: the estate has more than ten of them. So a
// second emulator holds the topics and queues of the Driver app, the ERP and the Carrier tracking path, and the apps that use them ask for it
// under the same connection name, "messaging". No app knows there are two. In Azure there is one namespace and both names are it.
var messagingEdge = builder.ExecutionContext.IsPublishMode ? messaging : builder.AddAzureServiceBus("messaging-2");

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
    // Receiver as well, to peek the subscriptions a reset waits on (ADR 0039); the TMS never receives from them.
    .WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataSender, ServiceBusBuiltInRole.AzureServiceBusDataReceiver)
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
    .WithReference(messagingEdge, "messaging").WaitFor(messagingEdge)
    .WithRoleAssignments(messagingEdge, ServiceBusBuiltInRole.AzureServiceBusDataSender)
    .WithHttpHealthCheck("/health");

// The Partner portal's presenter panel switches the Driver app's "unavailable" fault through its API (P11).
partnerPortal.WithEnvironment("DriverApp__Address", driverAppApi.GetEndpoint("http"));

// The Driver's screens: sign in as a seeded Driver, then a read-only list of that Driver's Trips. Locally a stand-in plays
// Entra ID's sign-in (ADR 0037); in Azure Easy Auth signs Drivers in.
var driverAppWeb = builder.AddProject<Projects.Norvia_DriverApp_Web>("driver-app-web")
    .WithReference(driverAppDatabase).WaitForCompletion(driverAppMigrations)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Driver app");

// The ERP is its own application, built and hosted like the TMS (ADR 0004), and meets the rest of the estate only through Service Bus (ADR 0001).
// Its migrations are a deployment step too. It loads its master data from the seed file as it starts and its outbox publishes its events (ADR 0003).
// Locally it signs the developer in as Finance; in Azure Easy Auth signs Finance in to the ERP's app registration.
var erpMigrations = builder.AddProject<Projects.Norvia_Erp_Migrations>("erp-migrations")
    .WithReference(erpDatabase).WaitFor(erpDatabase)
    .PublishAsAzureContainerAppJob();

var erpWeb = builder.AddProject<Projects.Norvia_Erp_Web>("erp-web")
    .WithReference(erpDatabase).WaitForCompletion(erpMigrations)
    .WithReference(messagingEdge, "messaging").WaitFor(messagingEdge)
    .WithRoleAssignments(messagingEdge, ServiceBusBuiltInRole.AzureServiceBusDataSender)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "ERP");

// The ERP billing API records the Billable lines the Billing Logic App hands over (E1). The ERP UI loads the master data it checks Shippers
// against, so the API waits for it; it publishes nothing yet, so it needs no Service Bus.
var erpApi = builder.AddProject<Projects.Norvia_Erp_Api>("erp-api")
    .WithReference(erpDatabase).WaitForCompletion(erpMigrations)
    .WaitFor(erpWeb)
    .WithHttpHealthCheck("/health");

// The Integration console, for the Integration operator. It has no database: it reads what the estate records (ADR 0017).
var integrationConsole = builder.AddProject<Projects.Norvia_IntegrationConsole_Web>("integration-console-web")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Integration console");

// The Simulator feeds the estate with representative orders, and only through its real doors (ADR 0001): the Partner API, as
// each Shipper's and Carrier's Persona, the TMS API, as the Planner, and the Driver app API, as each Driver. It has a database of
// its own for its runs and logs, and its migrations are a deployment step too. Locally it is on and signs everything itself; in
// Azure the estate switches it on, Easy Auth signs the presenter in and Entra issues every token (ADR 0042).
var simulatorDatabase = sql.AddDatabase("simulator");
var simulatorMigrations = builder.AddProject<Projects.Norvia_Simulator_Migrations>("simulator-migrations")
    .WithReference(simulatorDatabase).WaitFor(simulatorDatabase)
    .PublishAsAzureContainerAppJob();
var simulator = builder.AddProject<Projects.Norvia_Simulator_Web>("simulator-web")
    .WithReference(simulatorDatabase).WaitForCompletion(simulatorMigrations)
    .WithReference(tmsApi).WaitFor(tmsApi)
    .WaitFor(driverAppApi)
    .WaitFor(erpApi)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Simulator")
    .WithEnvironment("IntegrationConsole__Address", integrationConsole.GetEndpoint("http"))
    .WithEnvironment("TmsWeb__Address", tmsWeb.GetEndpoint("http"))
    .WithEnvironment("TmsApi__BaseAddress", tmsApi.GetEndpoint("http"))
    .WithEnvironment("DriverApp__BaseAddress", driverAppApi.GetEndpoint("http"))
    .WithEnvironment("ErpApi__BaseAddress", erpApi.GetEndpoint("http"));

// Locally the TMS, the ERP and the Partner portal read the repo's seed file; in Azure the copy each image carries, next to the
// app (the image's working directory).
var seedPath = builder.ExecutionContext.IsPublishMode ? Path.Combine("seed", "norvia.seed.json") : seedFile;
foreach (var readerOfTheSeedFile in new[] { tmsApi, tmsWeb, erpWeb, partnerPortal, driverAppApi, driverAppWeb, simulator })
{
    readerOfTheSeedFile.WithEnvironment("Seed__Path", seedPath);
}

// The TMS books the demo's days on its first start unless AppHost:SeedDemoData is false (user secrets), so a run can start with
// no Transport Orders and only the Simulator or a person creates them.
tmsApi.WithEnvironment("Seed__DemoData", (!bool.TryParse(builder.Configuration["AppHost:SeedDemoData"], out var seedDemoData) || seedDemoData).ToString());

var personas = SeedPersona.ReadAll(seedFile);

// The Simulator's Reset estate (ADR 0039) waits until the subscriptions whose consumers write records back are empty. The TMS and the Driver app
// peek them; the Partner portal consumes nothing from Service Bus. The names are the estate's, the same locally and in Azure.
tmsApi.WithEnvironment("Reset__QuietSubscriptions__0", "shipment-events/carrier-booking")
    .WithEnvironment("Reset__QuietSubscriptions__1", "shipment-events/shipper-notification")
    .WithEnvironment("Reset__QuietSubscriptions__2", "shipment-events/carrier-cancellation")
    .WithEnvironment("Reset__QuietSubscriptions__3", "trip-events/trip-dispatch");
driverAppApi.WithEnvironment("Reset__QuietSubscriptions__0", "driver-app-events/driver-reports");
simulator.WithEnvironment("PartnerPortal__BaseAddress", partnerPortal.GetEndpoint("http"));

if (builder.ExecutionContext.IsPublishMode)
{
    var estate = new Estate(builder);

    // Aspire owns the Container Apps environment, which logs to the estate's workspace so its retention is the
    // estate's too (integrationRetentionDays).
    builder.AddAzureContainerAppEnvironment("apps")
        .WithAzureLogAnalyticsWorkspace(builder.AddAzureLogAnalyticsWorkspace("logs").PublishAsExisting(estate.LogAnalyticsWorkspace, null));

    messaging.PublishAsExisting(estate.ServiceBusNamespace, null);

    var insights = builder.AddAzureApplicationInsights("insights").PublishAsExisting(estate.ApplicationInsights, null);
    foreach (var project in new[] { tmsMigrations, tmsApi, tmsWeb, partnerPortalMigrations, partnerPortal, driverAppMigrations, driverAppApi, driverAppWeb, erpMigrations, erpWeb, erpApi, integrationConsole, simulatorMigrations, simulator })
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
    // endpoint and invoice endpoint: the Logic Apps that call them are no browsers, and the endpoints ask them for the party's own credential.
    var personaEndpoints = personas.Select(persona => persona.EndpointPath)
        .Concat(personas.Select(persona => persona.CancellationPath).OfType<string>())
        .Concat(personas.Select(persona => persona.InvoicePath).OfType<string>())
        // The Simulator's Reset estate calls the portal's reset endpoint, which asks for the Simulator's key (ADR 0039).
        .Append("/reset/v1")
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
        .WithEnvironment("ConnectionStrings__driver-app-blobs", estate.DriverAppBlobEndpoint)
        .WithEnvironment("DriverEndpoint__ApiKey", driverAppSecrets.Resource.GetSecret("driver-app-endpoint-key"))
        .WithEnvironment("Authentication__Schemes__Bearer__Authority", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", estate.DriverAppApiClientId);

    // Drivers sign in to the Driver app with Entra ID. Easy Auth passes the Driver's token on, and the Driver ID is the app role
    // in it (ADR 0037). The presenter panel shows the Driver test users the estate made.
    // The Driver's screens store the files of a Proof of delivery in the estate's transit container (ADR 0009), as the identity the estate lets
    // write there. The connection is the container's storage account, reached with that identity.
    driverAppWeb.WithExternalHttpEndpoints()
        .WithAzureUserAssignedIdentity(driverAppIdentity)
        .WithEnvironment("ConnectionStrings__driver-app-blobs", estate.DriverAppBlobEndpoint)
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

    // The ERP opens to Finance's browser. Easy Auth signs them in to the ERP's own app registration, whose Finance app role the ERP checks,
    // as ADR 0026 has the Integration console do. It runs as the managed identity the estate made for it, which sends to Service Bus.
    var erpIdentity = builder.AddAzureUserAssignedIdentity("erp-identity")
        .PublishAsExisting(estate.ErpIdentity, null);
    // The billing API trusts Entra only. The Billing Logic App runs outside the Container Apps environment, so the API is reachable from
    // outside it, and Entra issues a token for its audience only to a principal holding the record role.
    erpApi.WithExternalHttpEndpoints()
        .WithEnvironment("Authentication__Schemes__Bearer__Authority", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", estate.EntraIssuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", estate.ErpApiClientId)
        .WithAzureUserAssignedIdentity(erpIdentity);
    erpWeb.WithExternalHttpEndpoints()
        .WithAzureUserAssignedIdentity(erpIdentity)
        .PublishAsAzureContainerApp((infrastructure, app) =>
            infrastructure.Add(EntraSignIn.For(infrastructure, app, estate.ErpRegistrationClientId, estate.EntraIssuer)));

    // The Simulator opens to the presenter's browser. Easy Auth signs them in to the Simulator's own app registration, whose app roles
    // the Simulator checks (ADR 0042, as ADR 0026 for the Integration console). It then reaches the estate as its own identity: the
    // Planner's token is the identity's, the Personas' and the Drivers' tokens are theirs, which trust it without a secret (ADR 0005),
    // and the Personas' subscription keys come from the estate's Key Vault. The estate switches it on and sets its caps.
    var simulatorIdentity = builder.AddAzureUserAssignedIdentity("simulator-identity")
        .PublishAsExisting(estate.SimulatorIdentity, null);
    simulator.WithExternalHttpEndpoints()
        .WithAzureUserAssignedIdentity(simulatorIdentity)
        .WithEnvironment("Simulator__Enabled", estate.SimulatorEnabled)
        .WithEnvironment("Simulator__MaxOrdersPerRun", estate.SimulatorMaxOrdersPerRun)
        .WithEnvironment("Simulator__MaxConcurrentRequests", estate.SimulatorMaxConcurrentRequests)
        .WithEnvironment("PartnerApi__BaseAddress", estate.PartnerApiUrl)
        .WithEnvironment("PartnerApi__PersonaClientIds", estate.PersonaClientIds)
        .WithEnvironment("PartnerApi__Entra__TenantId", estate.EntraTenantId)
        .WithEnvironment("PartnerApi__Entra__Scope", ReferenceExpression.Create($"{estate.PartnerApiClientId}/.default"))
        .WithEnvironment("PartnerApi__Entra__ManagedIdentityClientId", estate.SimulatorClientId)
        .WithEnvironment("TmsApi__Entra__Scope", ReferenceExpression.Create($"{estate.TmsApiClientId}/.default"))
        .WithEnvironment("TmsApi__Entra__ManagedIdentityClientId", estate.SimulatorClientId)
        .WithEnvironment("DriverApp__Entra__TenantId", estate.EntraTenantId)
        .WithEnvironment("DriverApp__Entra__Scope", ReferenceExpression.Create($"{estate.DriverAppApiClientId}/.default"))
        .WithEnvironment("DriverApp__Entra__ManagedIdentityClientId", estate.SimulatorClientId)
        .WithEnvironment("DriverApp__Entra__DriverClientIds", estate.DriverClientIds)
        // Finance's Invoice now and Credit note go to the ERP API as the Simulator's own identity, which holds the ERP API's manage role.
        .WithEnvironment("ErpApi__Entra__Scope", ReferenceExpression.Create($"{estate.ErpApiClientId}/.default"))
        .WithEnvironment("ErpApi__Entra__ManagedIdentityClientId", estate.SimulatorClientId)
        .PublishAsAzureContainerApp((infrastructure, app) =>
            infrastructure.Add(EntraSignIn.For(infrastructure, app, estate.SimulatorRegistrationClientId, estate.EntraIssuer)));
    foreach (var persona in personas)
    {
        simulator.WithEnvironment($"PartnerApi__SubscriptionKeys__{persona.Id}", partnerPortalSecrets.Resource.GetSecret($"subscription-key-{persona.Id}"));
    }

    // The Simulator reads each Shipper Persona's invoices inbox with the Shipper's own endpoint key, from the portal's vault.
    foreach (var shipper in personas.Where(persona => persona.IsShipper))
    {
        simulator.WithEnvironment($"PartnerPortal__ShipperEndpointKeys__{shipper.PartyId}", partnerPortalSecrets.Resource.GetSecret($"shipper-endpoint-key-{shipper.PartyId}"));
    }

    // The estate switches the reset on (resetEnabled, off by default). The Partner portal and the Simulator share a key from the portal's vault.
    tmsApi.WithEnvironment("Reset__Enabled", estate.ResetEnabled);
    driverAppApi.WithEnvironment("Reset__Enabled", estate.ResetEnabled);
    partnerPortal.WithEnvironment("Reset__Enabled", estate.ResetEnabled)
        .WithEnvironment("ResetEndpoint__ApiKey", partnerPortalSecrets.Resource.GetSecret("reset-endpoint-key"));
    simulator.WithEnvironment("PartnerPortal__ResetApiKey", partnerPortalSecrets.Resource.GetSecret("reset-endpoint-key"));
}
else
{
    // The emulator won't start a topic without a subscription. Monitoring is the consumer P1 and P3 name for their events.
    messaging.RunAsEmulator(emulator => emulator.WithConfiguration(ResubmitAwareFilters.Apply));
    messagingEdge.RunAsEmulator(emulator => emulator.WithConfiguration(ResubmitAwareFilters.Apply));
    var transportOrderEvents = messaging.AddServiceBusTopic("transport-order-events");
    transportOrderEvents.AddServiceBusSubscription("monitoring");
    // The E1 Billing Logic App's subscription: TransportOrderBillable only (see ResubmitAwareFilters).
    transportOrderEvents.AddServiceBusSubscription("billing");
    // Resource names are unique across the AppHost, so this monitoring subscription's resource is named for its topic.
    var shipmentEvents = messaging.AddServiceBusTopic("shipment-events");
    shipmentEvents.AddServiceBusSubscription("shipment-events-monitoring", subscriptionName: "monitoring");
    // The P4 booking Logic App's subscription: Carrier Shipments only (see ResubmitAwareFilters).
    shipmentEvents.AddServiceBusSubscription("carrier-booking");
    // The P7 Shipper notification Logic App's subscription: ShipperNotificationRequested only (see ResubmitAwareFilters).
    shipmentEvents.AddServiceBusSubscription("shipper-notification");
    // The P7 Carrier cancellation Logic App's subscription: CarrierBookingCancelled and CarrierBookingWithdrawn only (see ResubmitAwareFilters).
    shipmentEvents.AddServiceBusSubscription("carrier-cancellation");
    // The P9 Carrier cost Logic App's subscription: CarrierCostIncurred only (see ResubmitAwareFilters).
    shipmentEvents.AddServiceBusSubscription("carrier-cost");
    // The Trip events topic (P11). Monitoring is its first consumer, then the Trip dispatch Logic App with a subscription of its
    // own: TripDispatched only (see ResubmitAwareFilters).
    var tripEvents = messaging.AddServiceBusTopic("trip-events");
    tripEvents.AddServiceBusSubscription("trip-events-monitoring", subscriptionName: "monitoring");
    tripEvents.AddServiceBusSubscription("trip-dispatch");
    // The topic for Driver app events (P12). The Driver reports Logic App is the only consumer, with a subscription of its own that uses sessions
    // keyed by Trip number, so one Trip's reports are read one at a time and in order: the Driver's reports only (see ResubmitAwareFilters).
    messagingEdge.AddServiceBusTopic("driver-app-events")
        .AddServiceBusSubscription("driver-reports")
        .WithProperties(subscription => subscription.RequiresSession = true);
    // The topic for the ERP's invoicing events (E1). Monitoring is its first consumer; the E1 Shipper invoice Logic App has a subscription of its own:
    // ShipperInvoiceIssued only (see ResubmitAwareFilters).
    var invoicingEvents = messagingEdge.AddServiceBusTopic("invoicing-events");
    invoicingEvents.AddServiceBusSubscription("invoicing-events-monitoring", subscriptionName: "monitoring");
    invoicingEvents.AddServiceBusSubscription("shipper-invoice");
    // The tracking queue API Management puts a Carrier's tracking message on and the Carrier-tracking Logic App reads
    // (ADR 0008). A message the TMS refuses is dead-lettered; one it can't be given to is delivered again up to the
    // maximum delivery count, then dead-lettered by Service Bus itself. The estate Bicep sets the same count.
    messagingEdge.AddServiceBusQueue("carrier-tracking").WithProperties(queue => queue.MaxDeliveryCount = 10);
    // The Proof of delivery queue holds only the reference to a blob in the inbound container, which API Management stores a
    // Carrier's Proof of delivery in (ADR 0009). Azurite holds the container here; the estate Bicep makes it in Azure.
    messagingEdge.AddServiceBusQueue("carrier-proof-of-delivery").WithProperties(queue => queue.MaxDeliveryCount = 10);
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

    // Locally the Driver app's transit container is on Azurite, in the storage account the TMS's container is in. Its screens store the files; its API has the
    // same connection because its mediator holds the Deliver handler too.
    driverAppWeb.WithReference(blobs, "driver-app-blobs").WaitFor(blobs);
    driverAppApi.WithReference(blobs, "driver-app-blobs").WaitFor(blobs);

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
        .WithReference(messagingEdge, "messaging").WaitFor(messagingEdge)
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
    // Locally a worker plays the E1 Billing Logic App too. It reads the Billing subscription and records each Billable line with the ERP
    // billing API as the identity holding the record role.
    var billingWorker = builder.AddProject<Projects.Norvia_Integration_LocalBillingWorker>("billing-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(erpApi).WaitFor(erpApi)
        .WithEnvironment("Billing__ErpBaseAddress", erpApi.GetEndpoint("http"))
        .WithEnvironment("Billing__ErpTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("Billing__ErpTokens__Audience", LocalTokens.ErpAudience)
        .WithEnvironment("Billing__ErpTokens__ClientId", LocalTokens.BillingLogicAppClientId);
    // Locally a worker plays the P9 Carrier cost Logic App as well. It reads the Carrier cost subscription and records each Carrier cost with
    // the ERP billing API as the identity holding the Carrier cost record role.
    var carrierCostWorker = builder.AddProject<Projects.Norvia_Integration_LocalCarrierCostWorker>("carrier-cost-worker")
        .WithReference(messaging).WaitFor(messaging)
        .WithReference(erpApi).WaitFor(erpApi)
        .WithEnvironment("CarrierCost__ErpBaseAddress", erpApi.GetEndpoint("http"))
        .WithEnvironment("CarrierCost__ErpTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("CarrierCost__ErpTokens__Audience", LocalTokens.ErpAudience)
        .WithEnvironment("CarrierCost__ErpTokens__ClientId", LocalTokens.CarrierCostLogicAppClientId);
    // Locally a worker plays the E1 Shipper invoice Logic App too. It reads its subscription, sends each Shipper Persona's invoice to the
    // Persona's invoice endpoint in the Partner portal with a credential for the run, and reports it Sent to the ERP API as the identity
    // holding the report role.
    var shipperInvoiceWorker = builder.AddProject<Projects.Norvia_Integration_LocalShipperInvoiceWorker>("shipper-invoice-worker")
        .WithReference(messagingEdge, "messaging").WaitFor(messagingEdge)
        .WithReference(erpApi).WaitFor(erpApi)
        .WithEnvironment("ShipperInvoice__ErpBaseAddress", erpApi.GetEndpoint("http"))
        .WithEnvironment("ShipperInvoice__ErpTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("ShipperInvoice__ErpTokens__Audience", LocalTokens.ErpAudience)
        .WithEnvironment("ShipperInvoice__ErpTokens__ClientId", LocalTokens.ShipperInvoiceLogicAppClientId);
    // Locally a worker plays the P12 Driver reports Logic App as well. It reads the Driver app events subscription one Trip at a time, calls the
    // TMS as the identity holding the Trip start role, and tells the Driver app what became of each report as the identity holding the outcome
    // role.
    var driverReportsWorker = builder.AddProject<Projects.Norvia_Integration_LocalDriverReportsWorker>("driver-reports-worker")
        .WithReference(messagingEdge, "messaging").WaitFor(messagingEdge)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WaitFor(driverAppApi)
        .WithReference(blobs, "driver-app-blobs").WaitFor(blobs)
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
        .WithReference(messagingEdge, "messaging").WaitFor(messagingEdge)
        .WithReference(tmsApi).WaitFor(tmsApi)
        .WithEnvironment("Tracking__TmsBaseAddress", tmsApi.GetEndpoint("http"))
        .WithEnvironment("Tracking__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("Tracking__TmsTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("Tracking__TmsTokens__ClientId", LocalTokens.TrackingLogicAppClientId);
    // Locally a worker plays the Carrier Proof of delivery Logic App as well (ADR 0032). It reads the Proof of delivery queue
    // the stand-in fills, fetches the blob the stand-in stored, and records the Proof of delivery with the TMS as the
    // identity holding the record role.
    var proofOfDeliveryWorker = builder.AddProject<Projects.Norvia_Integration_LocalProofOfDeliveryWorker>("proof-of-delivery-worker")
        .WithReference(messagingEdge, "messaging").WaitFor(messagingEdge)
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
        // The invoice endpoint asks for the credential the notification endpoint does, as Key Vault holds one secret per Shipper in Azure.
        shipperInvoiceWorker
            .WithEnvironment($"ShipperInvoice__Shippers__{persona.PartyId}__Address", ReferenceExpression.Create($"{partnerPortal.GetEndpoint("http")}{persona.InvoicePath}"))
            .WithEnvironment($"ShipperInvoice__Shippers__{persona.PartyId}__ApiKey", shipperApiKey);
        partnerPortal.WithEnvironment($"ShipperEndpoint__ApiKeys__{persona.PartyId}", shipperApiKey);
        // The Simulator reads the Shipper's invoices inbox with the same key.
        simulator.WithEnvironment($"PartnerPortal__ShipperEndpointKeys__{persona.PartyId}", shipperApiKey);
    }

    // Locally every application takes the Simulator's Reset estate (ADR 0039); in Azure the estate switches it on. The Partner portal's reset takes a key,
    // as its other endpoints do, which the Simulator holds for the run.
    var resetApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    tmsApi.WithEnvironment("Reset__Enabled", "true");
    driverAppApi.WithEnvironment("Reset__Enabled", "true");
    partnerPortal.WithEnvironment("Reset__Enabled", "true")
        .WithEnvironment("ResetEndpoint__ApiKey", resetApiKey);
    simulator.WithEnvironment("PartnerPortal__ResetApiKey", resetApiKey);

    // Locally there is no Easy Auth either: the ERP signs the developer in as Finance.
    erpWeb.WithEnvironment("LocalUser__Name", $"{Environment.UserName} (local)")
        .WithEnvironment("LocalUser__Roles__0", "Finance");

    // Locally the ERP billing API trusts tokens signed with the same key. The Billing, Carrier cost and Shipper invoice Logic Apps' client IDs stand in for
    // their managed identities, the holders of the record and the report role.
    erpApi
        .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", LocalTokens.Issuer)
        .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", LocalTokens.ErpAudience)
        .WithEnvironment("Authentication__Schemes__Bearer__SigningKeys__0__Issuer", LocalTokens.Issuer)
        .WithUrlForEndpoint("http", url =>
        {
            url.Url = "/scalar";
            url.DisplayText = "API reference (Scalar)";
        });

    // Locally the Simulator is on, signs the developer in as an operator holding both app roles, and signs every token it sends itself,
    // with the key the local stand-ins trust (ADR 0042). It waits for the stand-in that plays API Management.
    simulator.WaitFor(partnerApi)
        .WithEnvironment("Simulator__Enabled", "true")
        .WithEnvironment("Simulator__MaxOrdersPerRun", "500")
        .WithEnvironment("Simulator__MaxConcurrentRequests", "4")
        .WithEnvironment("LocalOperator__Name", $"{Environment.UserName} (local)")
        .WithEnvironment("LocalOperator__Roles__0", "Simulator.Run")
        .WithEnvironment("LocalOperator__Roles__1", "Simulator.Read")
        .WithEnvironment("PartnerApi__BaseAddress", ReferenceExpression.Create($"{partnerApi.GetEndpoint("http")}/partner/v1"))
        // Every Persona: the Shippers book, and the Carriers answer, track and deliver.
        .WithEnvironment("PartnerApi__PersonaClientIds", string.Join(';', personas.Select(persona => $"{persona.Id}={LocalTokens.PersonaClientId(persona.Id)}")))
        .WithEnvironment("PartnerApi__LocalTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("PartnerApi__LocalTokens__Audience", LocalTokens.PartnerApiAudience)
        // The Planner signs its own token for the TMS API, as the TMS UI does: its client ID and the manage role.
        .WithEnvironment("TmsApi__LocalTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("TmsApi__LocalTokens__Audience", LocalTokens.TmsAudience)
        .WithEnvironment("TmsApi__LocalTokens__ClientId", LocalTokens.SimulatorClientId)
        // Each Driver works their Trips through the Driver app API with a token of their own, signed locally with the Driver ID as the one app role.
        .WithEnvironment("DriverApp__LocalTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("DriverApp__LocalTokens__Audience", LocalTokens.DriverAppAudience)
        .WithEnvironment("DriverApp__LocalTokens__ClientId", LocalTokens.SimulatorClientId)
        // Finance signs its own token for the ERP API: the Simulator's client ID and the manage role.
        .WithEnvironment("ErpApi__LocalTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment("ErpApi__LocalTokens__Audience", LocalTokens.ErpAudience)
        .WithEnvironment("ErpApi__LocalTokens__ClientId", LocalTokens.SimulatorClientId);

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
            // The Simulator books as the Shipper Personas, with the submit role and the same subscription key.
            simulator.WithEnvironment($"PartnerApi__SubscriptionKeys__{persona.Id}", subscriptionKey)
                .WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__0", LocalTokens.SubmitRole);
        }
        else
        {
            // A Carrier Persona answers the booking requests in its inbox, for the Carrier its client stands for.
            partnerApi.WithEnvironment($"PartnerApi__CarriersByClientId__{LocalTokens.PersonaClientId(persona.Id)}", persona.PartyId);
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__0", LocalTokens.RespondRole);
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__1", LocalTokens.TrackingSubmitRole);
            partnerPortal.WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__2", LocalTokens.ProofsOfDeliverySubmitRole);
            // The Simulator plays each Carrier as its Persona: it answers the booking, tracks the Shipment and proves the delivery, with the same subscription key.
            simulator.WithEnvironment($"PartnerApi__SubscriptionKeys__{persona.Id}", subscriptionKey)
                .WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__0", LocalTokens.RespondRole)
                .WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__1", LocalTokens.TrackingSubmitRole)
                .WithEnvironment($"PartnerApi__LocalTokens__Roles__{persona.Id}__2", LocalTokens.ProofsOfDeliverySubmitRole);
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
        (billingWorker.Resource, "Billing__ErpTokens__SigningKey"),
        (carrierCostWorker.Resource, "CarrierCost__ErpTokens__SigningKey"),
        (shipperInvoiceWorker.Resource, "ShipperInvoice__ErpTokens__SigningKey"),
        (driverReportsWorker.Resource, "DriverReports__TmsTokens__SigningKey"),
        (driverReportsWorker.Resource, "DriverReports__DriverApp__Tokens__SigningKey"),
        (driverAppApi.Resource, "Authentication__Schemes__Bearer__SigningKeys__0__Value"),
        (erpApi.Resource, "Authentication__Schemes__Bearer__SigningKeys__0__Value"),
        (trackingWorker.Resource, "Tracking__TmsTokens__SigningKey"),
        (proofOfDeliveryWorker.Resource, "ProofOfDelivery__TmsTokens__SigningKey"),
        (partnerPortal.Resource, "PartnerApi__LocalTokens__SigningKey"),
        (simulator.Resource, "PartnerApi__LocalTokens__SigningKey"),
        (simulator.Resource, "TmsApi__LocalTokens__SigningKey"),
        (simulator.Resource, "DriverApp__LocalTokens__SigningKey"),
        (simulator.Resource, "ErpApi__LocalTokens__SigningKey"),
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
