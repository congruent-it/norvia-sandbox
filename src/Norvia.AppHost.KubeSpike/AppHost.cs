// THROWAWAY SPIKE (#924): the local topology as plain containers and projects, published by Aspire's Kubernetes publisher.
using System.Security.Cryptography;
using Norvia.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddKubernetesEnvironment("k8s");

// SQL Server: one StatefulSet, five databases.
var sql = builder.AddSqlServer("sql").WithDataVolume();
var tmsDb = sql.AddDatabase("tms");
var portalDb = sql.AddDatabase("partner-portal");
var driverDb = sql.AddDatabase("driver-app");
var erpDb = sql.AddDatabase("erp");
var simDb = sql.AddDatabase("simulator");

// Azurite: a plain container; the apps read ConnectionStrings:blobs.
var azurite = builder.AddContainer("azurite", "mcr.microsoft.com/azure-storage/azurite", "3.34.0")
    .WithArgs("azurite-blob", "--blobHost", "0.0.0.0")
    .WithHttpEndpoint(targetPort: 10000, name: "blob")
    .WithVolume("azurite-data", "/data");
var blobs = builder.AddConnectionString("blobs", ReferenceExpression.Create(
    $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint={azurite.GetEndpoint("blob")}/devstoreaccount1;"));

// Two Service Bus emulators. Each needs SQL Server and a Config.json mounted from a file.
var sbPassword = builder.AddParameter("sb-sql-password", secret: true);
IResourceBuilder<ContainerResource> Emulator(string name)
{
    return builder.AddContainer(name, "mcr.microsoft.com/azure-messaging/servicebus-emulator", "latest")
        .WithEnvironment("ACCEPT_EULA", "Y")
        .WithEnvironment("SQL_SERVER", sql.Resource.Name)
        .WithEnvironment("MSSQL_SA_PASSWORD", sql.Resource.PasswordParameter)
        .WithContainerFiles("/ServiceBus_Emulator/ConfigFiles", [new ContainerFile { Name = "Config.json", Contents = File.ReadAllText(Path.Combine(builder.AppHostDirectory, "sb-config.json")) }])
        .WithEndpoint(targetPort: 5672, name: "amqp", scheme: "tcp")
        .WithHttpEndpoint(targetPort: 5300, name: "emulatorhealth")
        .WaitFor(sql);
}
var sb1 = Emulator("sb-messaging");
var sb2 = Emulator("sb-messaging-2");
IResourceBuilder<IResourceWithConnectionString> Bus(string name, IResourceBuilder<ContainerResource> e) =>
    builder.AddConnectionString(name, ReferenceExpression.Create(
        $"Endpoint=sb://{e.GetEndpoint("amqp").Property(EndpointProperty.Host)};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));
var messaging = Bus("messaging", sb1);
var messagingEdge = Bus("messaging-2", sb2);

var signingKey = builder.AddParameter(LocalTokens.SigningKeyParameter, new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);
var driverEndpointKey = builder.AddParameter("driver-app-endpoint-key", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var seed = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "seed", "norvia.seed.json"));

IResourceBuilder<ProjectResource> Migration<T>(string name, IResourceBuilder<SqlServerDatabaseResource> db) where T : IProjectMetadata, new() =>
    builder.AddProject<T>(name).WithReference(db).WaitFor(db);

var tmsMig = Migration<Projects.Norvia_Tms_Migrations>("tms-migrations", tmsDb);
var portalMig = Migration<Projects.Norvia_PartnerPortal_Migrations>("partner-portal-migrations", portalDb);
var driverMig = Migration<Projects.Norvia_DriverApp_Migrations>("driver-app-migrations", driverDb);
var erpMig = Migration<Projects.Norvia_Erp_Migrations>("erp-migrations", erpDb);
var simMig = Migration<Projects.Norvia_Simulator_Migrations>("simulator-migrations", simDb);

var tmsApi = builder.AddProject<Projects.Norvia_Tms_Api>("tms-api")
    .WithReference(tmsDb).WaitForCompletion(tmsMig).WithReference(messaging).WithReference(blobs)
    .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", LocalTokens.Issuer)
    .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", LocalTokens.TmsAudience)
    .WithEnvironment("Authentication__Schemes__Bearer__SigningKeys__0__Issuer", LocalTokens.Issuer)
    .WithEnvironment("Seed__Path", seed)
    .WithEnvironment("Reset__QuietSubscriptions__0", "shipment-events/carrier-booking")
    .WithHttpHealthCheck("/health").WithExternalHttpEndpoints();
var tmsWeb = builder.AddProject<Projects.Norvia_Tms_Web>("tms-web")
    .WithReference(tmsDb).WaitForCompletion(tmsMig).WithReference(tmsApi).WithReference(blobs)
    .WithEnvironment("TmsApi__BaseAddress", tmsApi.GetEndpoint("http")).WithHttpHealthCheck("/health").WithExternalHttpEndpoints();
var portal = builder.AddProject<Projects.Norvia_PartnerPortal_Web>("partner-portal-web")
    .WithReference(portalDb).WaitForCompletion(portalMig).WithHttpHealthCheck("/health").WithExternalHttpEndpoints();
var driverApi = builder.AddProject<Projects.Norvia_DriverApp_Api>("driver-app-api")
    .WithReference(driverDb).WaitForCompletion(driverMig).WithReference(messagingEdge, "messaging").WithReference(blobs, "driver-app-blobs")
    .WithEnvironment("DriverEndpoint__ApiKey", driverEndpointKey).WithHttpHealthCheck("/health");
var driverWeb = builder.AddProject<Projects.Norvia_DriverApp_Web>("driver-app-web")
    .WithReference(driverDb).WaitForCompletion(driverMig).WithReference(blobs, "driver-app-blobs").WithEnvironment("LocalSignIn__Enabled", "true").WithExternalHttpEndpoints();
var erpWeb = builder.AddProject<Projects.Norvia_Erp_Web>("erp-web")
    .WithReference(erpDb).WaitForCompletion(erpMig).WithReference(messagingEdge, "messaging").WithEnvironment("Seed__Path", seed).WithExternalHttpEndpoints();
var erpApi = builder.AddProject<Projects.Norvia_Erp_Api>("erp-api")
    .WithReference(erpDb).WaitForCompletion(erpMig).WithReference(messagingEdge, "messaging").WaitFor(erpWeb).WithHttpHealthCheck("/health");
var console = builder.AddProject<Projects.Norvia_IntegrationConsole_Web>("integration-console-web")
    .WithEnvironment("LocalOperator__Name", "local").WithEnvironment("LocalOperator__Roles__0", "IntegrationConsole.Read")
    .WithEnvironment("LocalServiceBus__Namespaces__messaging__ConnectionString", messaging.Resource.ConnectionStringExpression)
    .WithEnvironment("LocalServiceBus__Namespaces__messaging-edge__ConnectionString", messagingEdge.Resource.ConnectionStringExpression)
    .WithExternalHttpEndpoints();
var simulator = builder.AddProject<Projects.Norvia_Simulator_Web>("simulator-web")
    .WithReference(simDb).WaitForCompletion(simMig)
    .WithEnvironment("TmsApi__BaseAddress", tmsApi.GetEndpoint("http"))
    .WithEnvironment("ErpApi__BaseAddress", erpApi.GetEndpoint("http"))
    .WithEnvironment("DriverApp__BaseAddress", driverApi.GetEndpoint("http"))
    .WithEnvironment("PartnerPortal__BaseAddress", portal.GetEndpoint("http"))
    .WithExternalHttpEndpoints();
var healthCheck = builder.AddProject<Projects.Norvia_HealthCheck_Web>("health-check-web")
    .WithEnvironment("IntegrationConsole__Address", console.GetEndpoint("http")).WithEnvironment("Estate__Snapshot", "as-found").WithExternalHttpEndpoints();
var partnerApi = builder.AddProject<Projects.Norvia_Integration_LocalPartnerApi>("partner-api")
    .WithReference(tmsApi).WithReference(messagingEdge, "messaging").WithReference(blobs).WithHttpHealthCheck("/health").WithExternalHttpEndpoints();

// The 14 workers, wired the way the AppHost does: a Service Bus connection, the API each calls, and a token client ID.
IResourceBuilder<ProjectResource> Worker<T>(string name, bool edge, string section, IResourceBuilder<ProjectResource> target, string clientId, string audience) where T : IProjectMetadata, new() =>
    builder.AddProject<T>(name)
        .WithReference(edge ? messagingEdge : messaging, "messaging").WaitFor(target)
        .WithEnvironment($"{section}__TmsBaseAddress", target.GetEndpoint("http"))
        .WithEnvironment($"{section}__TmsTokens__Issuer", LocalTokens.Issuer)
        .WithEnvironment($"{section}__TmsTokens__Audience", audience)
        .WithEnvironment($"{section}__TmsTokens__ClientId", clientId)
        .WithEnvironment($"{section}__TmsTokens__SigningKey", signingKey);
var booking = Worker<Projects.Norvia_Integration_LocalBookingWorker>("booking-worker", false, "Booking", tmsApi, LocalTokens.BookingLogicAppClientId, LocalTokens.TmsAudience);
Worker<Projects.Norvia_Integration_LocalShipperNotificationWorker>("shipper-notification-worker", false, "ShipperNotification", tmsApi, LocalTokens.ShipperNotificationLogicAppClientId, LocalTokens.TmsAudience);
Worker<Projects.Norvia_Integration_LocalCarrierCancellationWorker>("carrier-cancellation-worker", false, "CarrierCancellation", tmsApi, LocalTokens.CarrierCancellationLogicAppClientId, LocalTokens.TmsAudience);
Worker<Projects.Norvia_Integration_LocalTripDispatchWorker>("trip-dispatch-worker", false, "TripDispatch", tmsApi, LocalTokens.TripDispatchLogicAppClientId, LocalTokens.TmsAudience)
    .WithEnvironment("TripDispatch__DriverApp__ApiKey", driverEndpointKey);
Worker<Projects.Norvia_Integration_LocalBillingWorker>("billing-worker", false, "Billing", erpApi, LocalTokens.BillingLogicAppClientId, LocalTokens.ErpAudience);
Worker<Projects.Norvia_Integration_LocalMasterDataSyncWorker>("master-data-sync-worker", true, "MasterDataSync", tmsApi, LocalTokens.MasterDataSyncLogicAppClientId, LocalTokens.TmsAudience);
Worker<Projects.Norvia_Integration_LocalCarrierCostWorker>("carrier-cost-worker", false, "CarrierCost", erpApi, LocalTokens.CarrierCostLogicAppClientId, LocalTokens.ErpAudience);
Worker<Projects.Norvia_Integration_LocalShipperInvoiceWorker>("shipper-invoice-worker", false, "ShipperInvoice", erpApi, LocalTokens.ShipperInvoiceLogicAppClientId, LocalTokens.ErpAudience);
Worker<Projects.Norvia_Integration_LocalCarrierInvoiceOutcomeWorker>("carrier-invoice-outcome-worker", true, "CarrierInvoiceOutcome", erpApi, LocalTokens.CarrierInvoiceOutcomeLogicAppClientId, LocalTokens.ErpAudience);
Worker<Projects.Norvia_Integration_LocalCarrierInvoiceWorker>("carrier-invoice-worker", false, "CarrierInvoice", erpApi, LocalTokens.CarrierInvoiceLogicAppClientId, LocalTokens.ErpAudience);
// The Partner portal and this worker share a file (the "driver reports disabled" fault flag): a path in the AppHost's temp dir.
var flag = Path.Combine(Path.GetTempPath(), $"norvia-driver-reports-disabled-{Guid.NewGuid():N}");
Worker<Projects.Norvia_Integration_LocalDriverReportsWorker>("driver-reports-worker", true, "DriverReports", tmsApi, LocalTokens.DriverReportsLogicAppClientId, LocalTokens.TmsAudience)
    .WithEnvironment("DriverReports__DisabledFlagPath", flag);
Worker<Projects.Norvia_Integration_LocalTrackingWorker>("tracking-worker", true, "Tracking", tmsApi, LocalTokens.TrackingLogicAppClientId, LocalTokens.TmsAudience);
Worker<Projects.Norvia_Integration_LocalProofOfDeliveryWorker>("proof-of-delivery-worker", true, "ProofOfDelivery", tmsApi, LocalTokens.ProofOfDeliveryLogicAppClientId, LocalTokens.TmsAudience)
    .WithReference(blobs);
// Per-run random Carrier keys, as the AppHost generates them.
var carrierKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
booking.WithEnvironment("Booking__Carriers__C1__ApiKey", carrierKey);
portal.WithEnvironment("CarrierEndpoint__ApiKeys__C1", carrierKey).WithEnvironment("Faults__Owners__tms__Address", tmsApi.GetEndpoint("http"))
    .WithEnvironment("DriverReports__DisabledFlagPath", flag);

builder.Build().Run();
