using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Provisioning.AppContainers;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>Checks the AppHost's resource model without starting anything, as it runs locally and as azd deploys it.</summary>
public sealed class AppHostModelTests
{
    /// <summary>The UIs a presenter shows side by side, which have no login of their own (ADR 0024).</summary>
    public static TheoryData<string> DemoUis => ["tms-web", "partner-portal-web"];

    [Fact]
    public async Task EveryTopic_HasASubscription_SoTheServiceBusEmulatorStarts()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var subscribedTopics = builder.Resources.OfType<AzureServiceBusSubscriptionResource>().Select(s => s.Parent).ToHashSet();

        builder.Resources.OfType<AzureServiceBusTopicResource>().Should().OnlyContain(topic => subscribedTopics.Contains(topic));
    }

    [Theory]
    [InlineData("transport-order-events")]
    [InlineData("shipment-events")]
    [InlineData("trip-events")]
    public async Task Locally_AnEventsTopic_HasAMonitoringSubscription(string topicName)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var topic = builder.Resources.OfType<AzureServiceBusTopicResource>().Should().ContainSingle(t => t.TopicName == topicName).Subject;

        builder.Resources.OfType<AzureServiceBusSubscriptionResource>()
            .Should().Contain(subscription => subscription.Parent == topic && subscription.SubscriptionName == "monitoring");
    }

    [Theory]
    [InlineData("tms-api", "tms-migrations")]
    [InlineData("tms-web", "tms-migrations")]
    [InlineData("partner-portal-web", "partner-portal-migrations")]
    public async Task Locally_AnApp_StartsOnceItsDatabaseIsMigrated(string app, string migrations)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        Resource(builder, app).Annotations.OfType<WaitAnnotation>()
            .Should().Contain(wait => wait.Resource.Name == migrations && wait.WaitType == WaitType.WaitForCompletion);
    }

    [Fact]
    public async Task Locally_ThePartnerPortal_ReachesNorviaThroughTheLocalPartnerApi_WithTokensItSignsItself()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");

        portal.Should().Contain("PartnerApi__BaseAddress", "{partner-api.bindings.http.url}/partner/v1")
            .And.Contain("PartnerApi__PersonaClientIds", "brouwer-dranken=norvia-local-persona-brouwer-dranken;tuincentrum-groenrijk=norvia-local-persona-tuincentrum-groenrijk;van-dijk-transport=norvia-local-persona-van-dijk-transport;kessler-spedition=norvia-local-persona-kessler-spedition;pakketpunt-express=norvia-local-persona-pakketpunt-express")
            .And.Contain("PartnerApi__LocalTokens__Roles__brouwer-dranken__0", "TransportOrders.Submit")
            .And.Contain("PartnerApi__LocalTokens__Roles__brouwer-dranken__1", "Tracking.Read", "a Shipper Persona reads its tracking (P8)")
            .And.Contain("PartnerApi__LocalTokens__Roles__tuincentrum-groenrijk__1", "Tracking.Read")
            .And.ContainKey("PartnerApi__LocalTokens__SigningKey");
        foreach (var carrier in new[] { "van-dijk-transport", "kessler-spedition", "pakketpunt-express" })
        {
            portal.Should().Contain($"PartnerApi__LocalTokens__Roles__{carrier}__0", "CarrierBookings.Respond", "a Carrier Persona answers its booking requests");
        }

        portal.Keys.Should().NotContain(key => key.StartsWith("PartnerApi__LocalTokens__Roles__brouwer-dranken__2", StringComparison.Ordinal), "a Shipper Persona holds no respond role");
        portal.Where(entry => entry.Key.Contains("LocalTokens__Roles__van-dijk-transport", StringComparison.Ordinal) || entry.Key.Contains("LocalTokens__Roles__kessler-spedition", StringComparison.Ordinal)
                || entry.Key.Contains("LocalTokens__Roles__pakketpunt-express", StringComparison.Ordinal))
            .Should().NotContain(entry => entry.Value == "Tracking.Read", "a Carrier Persona is refused tracking with 403");
        portal.Keys.Should().NotContain(key => key.StartsWith("PartnerApi__Entra", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Locally_ThePartnerPortal_AsksEachPersonasEndpointCallerForACredential()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");

        portal.Keys.Where(key => key.StartsWith("ShipperEndpoint__ApiKeys__", StringComparison.Ordinal))
            .Should().BeEquivalentTo("ShipperEndpoint__ApiKeys__SHP-0001", "ShipperEndpoint__ApiKeys__SHP-0003");
        portal.Keys.Where(key => key.StartsWith("CarrierEndpoint__ApiKeys__", StringComparison.Ordinal))
            .Should().BeEquivalentTo("CarrierEndpoint__ApiKeys__CAR-0001", "CarrierEndpoint__ApiKeys__CAR-0002", "CarrierEndpoint__ApiKeys__CAR-0003");
        portal.Values.Where(value => value.Length == 32).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Locally_TheTmsUi_ChangesThingsThroughTheTmsApi_WithATokenItSignsItself()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var tmsUi = await EnvironmentOfAsync(builder, "tms-web");

        tmsUi.Should().Contain("TmsApi__BaseAddress", "{tms-api.bindings.http.url}")
            .And.Contain("TmsApi__LocalTokens__Issuer", "https://norvia.local/tokens")
            .And.Contain("TmsApi__LocalTokens__Audience", "api://norvia-tms")
            .And.Contain("TmsApi__LocalTokens__ClientId", "norvia-local-tms-ui")
            .And.ContainKey("TmsApi__LocalTokens__SigningKey");
        tmsUi.Keys.Should().NotContain(key => key.StartsWith("TmsApi__Entra", StringComparison.Ordinal));
        Resource(builder, "tms-web").Annotations.OfType<WaitAnnotation>().Should().Contain(wait => wait.Resource.Name == "tms-api");
    }

    [Fact]
    public async Task Locally_EveryPersona_HasOneSubscriptionKey_ThatTheLocalPartnerApiKnows()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");
        var partnerApi = await EnvironmentOfAsync(builder, "partner-api");

        foreach (var persona in new[] { "brouwer-dranken", "van-dijk-transport", "kessler-spedition", "pakketpunt-express" })
        {
            var key = $"PartnerApi__SubscriptionKeys__{persona}";
            portal[key].Should().NotBeNullOrEmpty().And.Be(partnerApi[key]);
        }

        partnerApi.Should().Contain("PartnerApi__ShippersByClientId__norvia-local-persona-brouwer-dranken", "SHP-0001");
        partnerApi.Keys.Should().NotContain(key => key.Contains("van-dijk-transport", StringComparison.Ordinal) && key.Contains("ShippersByClientId", StringComparison.Ordinal));
        partnerApi.Should().Contain("PartnerApi__CarriersByClientId__norvia-local-persona-van-dijk-transport", "CAR-0001")
            .And.Contain("PartnerApi__CarriersByClientId__norvia-local-persona-kessler-spedition", "CAR-0002")
            .And.Contain("PartnerApi__CarriersByClientId__norvia-local-persona-pakketpunt-express", "CAR-0003");
        partnerApi.Keys.Should().NotContain(key => key.Contains("brouwer-dranken", StringComparison.Ordinal) && key.Contains("CarriersByClientId", StringComparison.Ordinal));
        partnerApi.Should().Contain("Tms__AnswerClientId", "norvia-local-answer-logic-app")
            .And.Contain("Tms__ShipperTrackingClientId", "norvia-local-api-management");
    }

    [Fact]
    public async Task Locally_ATrackingWorker_PlaysTheCarrierTrackingLogicApp_AsTheIdentityHoldingTheRecordRole_AndTheQueueIsThere()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var worker = await EnvironmentOfAsync(builder, "tracking-worker");
        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");

        worker.Should().Contain("Tracking__TmsTokens__ClientId", "norvia-local-tracking-logic-app")
            .And.Contain("Tracking__TmsTokens__Audience", "api://norvia-tms")
            .And.ContainKey("Tracking__TmsTokens__SigningKey");
        builder.Resources.OfType<AzureServiceBusQueueResource>().Should().ContainSingle(queue => queue.QueueName == "carrier-tracking");
        portal.Should().Contain("PartnerApi__LocalTokens__Roles__van-dijk-transport__1", "Tracking.Submit");
    }

    [Fact]
    public async Task Locally_ATripDispatchWorker_PlaysTheTripDispatchLogicApp_AsTheIdentityHoldingTheReportRole_WithTheDriverAppsCredential()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var worker = await EnvironmentOfAsync(builder, "trip-dispatch-worker");
        var driverApp = await EnvironmentOfAsync(builder, "driver-app-api");

        worker.Should().Contain("TripDispatch__TmsTokens__ClientId", "norvia-local-trip-dispatch-logic-app")
            .And.Contain("TripDispatch__TmsTokens__Audience", "api://norvia-tms")
            .And.Contain("TripDispatch__DriverApp__Address", "{driver-app-api.bindings.http.url}/v1/trips")
            .And.ContainKey("TripDispatch__TmsTokens__SigningKey");
        worker["TripDispatch__DriverApp__ApiKey"].Should().Be(driverApp["DriverEndpoint__ApiKey"], "the Driver app asks its caller for the credential the worker has");
        var topic = builder.Resources.OfType<AzureServiceBusTopicResource>().Single(t => t.TopicName == "trip-events");
        builder.Resources.OfType<AzureServiceBusSubscriptionResource>()
            .Should().Contain(subscription => subscription.Parent == topic && subscription.SubscriptionName == "trip-dispatch");
        Resource(builder, "trip-dispatch-worker").Annotations.OfType<WaitAnnotation>()
            .Should().Contain(wait => wait.Resource.Name == "driver-app-api");
    }

    [Fact]
    public async Task Locally_Azurite_StandsInForTheTmsBlobStorage_AndTheTmsWaitsForIt()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var storage = builder.Resources.OfType<AzureStorageResource>().Single();
        storage.IsEmulator.Should().BeTrue();
        Resource(builder, "tms-api").Annotations.OfType<WaitAnnotation>().Should().Contain(wait => wait.Resource.Name == "blobs");
        (await EnvironmentOfAsync(builder, "tms-api")).Keys.Should().Contain("ConnectionStrings__blobs");
    }

    [Fact]
    public async Task InAzure_TheTms_HasABlobContainerForProofOfDeliveryFiles_WhoseBlobsMoveToCoolAfter30Days()
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.OfType<AzureBlobStorageContainerResource>().Should().ContainSingle(container => container.BlobContainerName == "proofs-of-delivery",
            "the inbound container is the estate Bicep's, not Aspire's");
        var bicep = builder.Resources.OfType<AzureStorageResource>().Single().GetBicepTemplateString();
        bicep.Should().Contain("Microsoft.Storage/storageAccounts/managementPolicies@")
            .And.Contain("tierToCool")
            .And.MatchRegex(@"daysAfterModificationGreaterThan: 30\b")
            .And.Contain("proofs-of-delivery/");
        bicep.Should().NotContain("tierToArchive", "the files are kept, never archived or deleted").And.NotContain("delete:");
    }

    [Fact]
    public async Task InAzure_TheTmsApi_CanWriteItsBlobs_AndTheTmsUiOnlyReadThem()
    {
        await using var builder = await CreateForAzureAsync();

        (await EnvironmentOfAsync(builder, "tms-api")).Keys.Should().Contain("ConnectionStrings__blobs");
        (await EnvironmentOfAsync(builder, "tms-web")).Keys.Should().Contain("ConnectionStrings__blobs");
        Resource(builder, "tms-api").Annotations.OfType<Aspire.Hosting.Azure.RoleAssignmentAnnotation>()
            .Should().ContainSingle(role => role.Target is AzureStorageResource).Which.Roles.Select(r => r.Name).Should().Equal("StorageBlobDataContributor");
        Resource(builder, "tms-web").Annotations.OfType<Aspire.Hosting.Azure.RoleAssignmentAnnotation>()
            .Should().ContainSingle(role => role.Target is AzureStorageResource).Which.Roles.Select(r => r.Name).Should().Equal("StorageBlobDataReader");
    }

    [Fact]
    public async Task Locally_ADriverReportsWorker_PlaysTheDriverReportsLogicApp_OnASubscriptionWithSessions_AsTheIdentityHoldingTheStartAndOutcomeRoles()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var worker = await EnvironmentOfAsync(builder, "driver-reports-worker");
        var driverApp = await EnvironmentOfAsync(builder, "driver-app-api");

        worker.Should().Contain("DriverReports__TmsTokens__ClientId", "norvia-local-driver-reports-logic-app")
            .And.Contain("DriverReports__TmsTokens__Audience", "api://norvia-tms")
            .And.Contain("DriverReports__DriverApp__Tokens__ClientId", "norvia-local-driver-reports-logic-app")
            .And.Contain("DriverReports__DriverApp__Tokens__Audience", "api://norvia-driver-app")
            .And.Contain("DriverReports__DriverApp__BaseAddress", "{driver-app-api.bindings.http.url}")
            .And.ContainKey("DriverReports__TmsTokens__SigningKey")
            .And.ContainKey("DriverReports__DriverApp__Tokens__SigningKey");
        driverApp.Should().Contain("Authentication__Schemes__Bearer__ValidAudiences__0", "api://norvia-driver-app")
            .And.Contain("Authentication__Schemes__Bearer__ValidIssuer", "https://norvia.local/tokens")
            .And.ContainKey("Authentication__Schemes__Bearer__SigningKeys__0__Value");
        driverApp["Authentication__Schemes__Bearer__SigningKeys__0__Value"].Should().Be(
            worker["DriverReports__DriverApp__Tokens__SigningKey"], "the Driver app trusts the key the worker signs its outcome reports with");

        var topic = builder.Resources.OfType<AzureServiceBusTopicResource>().Single(t => t.TopicName == "driver-app-events");
        var subscription = builder.Resources.OfType<AzureServiceBusSubscriptionResource>().Should()
            .ContainSingle(subscription => subscription.Parent == topic && subscription.SubscriptionName == "driver-reports").Subject;
        subscription.RequiresSession.Should().BeTrue("one Trip's reports are read one at a time and in order (ADR 0016)");
        builder.Resources.OfType<AzureServiceBusSubscriptionResource>().Where(s => s.Parent == topic).Should().ContainSingle("the Driver reports Logic App is the only consumer");
        Resource(builder, "driver-reports-worker").Annotations.OfType<WaitAnnotation>().Should().Contain(wait => wait.Resource.Name == "driver-app-api");
        Resource(builder, "driver-app-api").Annotations.OfType<WaitAnnotation>().Should().Contain(wait => wait.Resource.Name == "messaging", "its outbox publishes to Service Bus");
    }

    [Fact]
    public async Task InAzure_TheDriverAppsOutcomeEndpoint_TrustsEntra_ForTheDriverAppApisAppRegistration_AndItsOutboxMaySendToServiceBus()
    {
        await using var builder = await CreateForAzureAsync();

        var driverApp = await EnvironmentOfAsync(builder, "driver-app-api");

        driverApp.Should().Contain("Authentication__Schemes__Bearer__ValidAudiences__0", "{estate-driver-app-api-client-id.value}")
            .And.Contain("Authentication__Schemes__Bearer__Authority", "{estate-entra-issuer.value}")
            .And.NotContainKey("Authentication__Schemes__Bearer__SigningKeys__0__Value");
        Resource(builder, "driver-app-api").Annotations.OfType<Aspire.Hosting.Azure.RoleAssignmentAnnotation>()
            .Should().ContainSingle(role => role.Target is AzureServiceBusResource).Which.Roles.Select(r => r.Name).Should().Equal("AzureServiceBusDataSender");
        builder.Resources.Should().NotContain(resource => resource.Name == "driver-reports-worker", "the Logic App is in the estate");
    }

    [Fact]
    public async Task Locally_AProofOfDeliveryWorker_PlaysTheCarrierProofOfDeliveryLogicApp_AsTheIdentityHoldingTheRecordRole_AndTheQueueAndTheInboundContainerAreThere()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var worker = await EnvironmentOfAsync(builder, "proof-of-delivery-worker");
        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");

        worker.Should().Contain("ProofOfDelivery__TmsTokens__ClientId", "norvia-local-proof-of-delivery-logic-app")
            .And.Contain("ProofOfDelivery__TmsTokens__Audience", "api://norvia-tms")
            .And.ContainKey("ProofOfDelivery__TmsTokens__SigningKey");
        builder.Resources.OfType<AzureServiceBusQueueResource>().Should().ContainSingle(queue => queue.QueueName == "carrier-proof-of-delivery");
        builder.Resources.OfType<AzureBlobStorageContainerResource>().Should().Contain(container => container.BlobContainerName == "inbound-proofs-of-delivery");
        portal.Should().Contain("PartnerApi__LocalTokens__Roles__van-dijk-transport__2", "ProofsOfDelivery.Submit")
            .And.Contain("PartnerApi__LocalTokens__Roles__kessler-spedition__2", "ProofsOfDelivery.Submit")
            .And.Contain("PartnerApi__LocalTokens__Roles__pakketpunt-express__2", "ProofsOfDelivery.Submit");
    }

    [Fact]
    public async Task Locally_ADatabaseViewer_RunsBesideSql_ToInspectTheDatabases()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        builder.Resources.Should().Contain(resource => resource.Name.Contains("dbgate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InAzure_NoDatabaseViewerIsDeployed()
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.Should().NotContain(resource => resource.Name.Contains("dbgate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InAzure_ServiceBus_IsTheEstatesNamespace_WithTheEstatesTopics()
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.OfType<AzureServiceBusResource>().Single().IsExisting().Should().BeTrue();
        builder.Resources.OfType<AzureServiceBusTopicResource>().Should().BeEmpty("the estate Bicep owns the topics (ADR 0004)");
        builder.Resources.OfType<AzureServiceBusSubscriptionResource>().Should().BeEmpty("the estate Bicep owns the subscriptions (ADR 0004)");
    }

    [Fact]
    public async Task InAzure_TheTmsDatabase_IsAzureSql()
    {
        await using var builder = await CreateForAzureAsync();

        var sql = builder.Resources.OfType<AzureSqlServerResource>().Single();

        sql.IsContainer().Should().BeFalse();
        builder.Resources.OfType<AzureSqlDatabaseResource>().Should().ContainSingle(database => database.Name == "tms");
    }

    [Fact]
    public async Task InAzure_EveryDatabase_KeepsRunningOnceItsFreeAllowanceIsUsed()
    {
        await using var builder = await CreateForAzureAsync();

        var bicep = builder.Resources.OfType<AzureSqlServerResource>().Single().GetBicepTemplateString();

        Regex.Count(bicep, "useFreeLimit: true").Should().Be(3, "all three databases stay on the free offer");
        Regex.Count(bicep, "freeLimitExhaustionBehavior: 'BillOverUsage'").Should().Be(3, "a paused database fails the migrations and the demo");
    }

    [Fact]
    public async Task InAzure_TheTmsApi_TrustsEntraAndOnlyTheEstatesIntakeCaller()
    {
        await using var builder = await CreateForAzureAsync();

        var environment = await EnvironmentOfAsync(builder, "tms-api");

        environment.Should().Contain("Authentication__Schemes__Bearer__Authority", "{estate-entra-issuer.value}")
            .And.Contain("Authentication__Schemes__Bearer__ValidIssuer", "{estate-entra-issuer.value}")
            .And.Contain("Authentication__Schemes__Bearer__ValidAudiences__0", "{estate-tms-api-client-id.value}")
            .And.Contain("IntakeCallers__ClientIdsByChannel__ShipperApi__0", "{estate-intake-client-id.value}");
        environment.Keys.Should().NotContain(key => key.Contains("SigningKeys", StringComparison.Ordinal), "only Entra issues tokens in Azure");
        builder.Resources.OfType<ParameterResource>().Should().NotContain(parameter => parameter.Name == "local-token-signing-key");
    }

    [Theory]
    [InlineData("tms-api")]
    [InlineData("tms-web")]
    [InlineData("tms-migrations")]
    [InlineData("partner-portal-web")]
    [InlineData("partner-portal-migrations")]
    public async Task InAzure_AnApp_SendsItsTelemetryToTheEstatesApplicationInsights(string tmsResource)
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.OfType<AzureApplicationInsightsResource>().Single().IsExisting().Should().BeTrue();
        (await EnvironmentOfAsync(builder, tmsResource)).Should().ContainKey("APPLICATIONINSIGHTS_CONNECTION_STRING");
    }

    [Fact]
    public async Task InAzure_ThePartnerPortal_BooksThroughApiManagement_AsTheIdentityThePersonasTrust()
    {
        await using var builder = await CreateForAzureAsync();

        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");

        portal.Should().Contain("PartnerApi__BaseAddress", "{estate-partner-api-url.value}")
            .And.Contain("PartnerApi__PersonaClientIds", "{estate-persona-client-ids.value}")
            .And.Contain("PartnerApi__Entra__TenantId", "{estate-entra-tenant-id.value}")
            .And.Contain("PartnerApi__Entra__Scope", "{estate-partner-api-client-id.value}/.default")
            .And.Contain("PartnerApi__Entra__ManagedIdentityClientId", "{estate-partner-portal-client-id.value}");
        portal.Keys.Should().NotContain(key => key.StartsWith("PartnerApi__LocalTokens", StringComparison.Ordinal), "only Entra issues tokens in Azure");
        Resource(builder, "partner-portal-web").Annotations.OfType<AppIdentityAnnotation>()
            .Should().ContainSingle().Which.IdentityResource.Should().BeOfType<AzureUserAssignedIdentityResource>().Which.Name.Should().Be("partner-portal-identity");
        builder.Resources.OfType<AzureUserAssignedIdentityResource>().Single(identity => identity.Name == "partner-portal-identity")
            .IsExisting().Should().BeTrue("the estate owns the identity, because the Personas' app registrations trust it");
    }

    [Fact]
    public async Task InAzure_TheTmsUi_ChangesThingsThroughTheTmsApi_AsItsOwnIdentity()
    {
        await using var builder = await CreateForAzureAsync();

        var tmsUi = await EnvironmentOfAsync(builder, "tms-web");

        tmsUi.Should().Contain("TmsApi__BaseAddress", "{tms-api.bindings.http.url}")
            .And.Contain("TmsApi__Entra__Scope", "{estate-tms-api-client-id.value}/.default")
            .And.Contain("TmsApi__Entra__ManagedIdentityClientId", "{estate-tms-ui-client-id.value}");
        tmsUi.Keys.Should().NotContain(key => key.StartsWith("TmsApi__LocalTokens", StringComparison.Ordinal), "only Entra issues tokens in Azure");
        Resource(builder, "tms-web").Annotations.OfType<AppIdentityAnnotation>()
            .Should().ContainSingle().Which.IdentityResource.Should().BeOfType<AzureUserAssignedIdentityResource>().Which.Name.Should().Be("tms-ui-identity");
        builder.Resources.OfType<AzureUserAssignedIdentityResource>().Single(identity => identity.Name == "tms-ui-identity")
            .IsExisting().Should().BeTrue("the estate owns the identity, because it grants it the TMS API's manage role");
    }

    [Fact]
    public async Task InAzure_ThePersonasSubscriptionKeys_ComeFromTheEstatesKeyVault()
    {
        await using var builder = await CreateForAzureAsync();

        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");

        builder.Resources.OfType<AzureKeyVaultResource>().Single(vault => vault.Name == "partner-portal-secrets").IsExisting().Should().BeTrue();
        foreach (var persona in new[] { "brouwer-dranken", "van-dijk-transport", "kessler-spedition", "pakketpunt-express" })
        {
            portal[$"PartnerApi__SubscriptionKeys__{persona}"].Should().Contain($"subscription-key-{persona}");
        }
    }

    [Fact]
    public async Task InAzure_TheDriverApp_AsksItsCallerForTheCredentialInTheEstatesKeyVault_AsItsOwnIdentity()
    {
        await using var builder = await CreateForAzureAsync();

        var driverApp = await EnvironmentOfAsync(builder, "driver-app-api");

        driverApp["DriverEndpoint__ApiKey"].Should().Contain("driver-app-endpoint-key");
        builder.Resources.OfType<AzureKeyVaultResource>().Single(vault => vault.Name == "driver-app-secrets").IsExisting().Should().BeTrue("the estate owns the vault");
        Resource(builder, "driver-app-api").Annotations.OfType<AppIdentityAnnotation>()
            .Should().ContainSingle().Which.IdentityResource.Should().BeOfType<AzureUserAssignedIdentityResource>().Which.Name.Should().Be("driver-app-identity");
        builder.Resources.OfType<AzureUserAssignedIdentityResource>().Single(identity => identity.Name == "driver-app-identity")
            .IsExisting().Should().BeTrue("the estate owns the identity, because it grants it the credential");
        Resource(builder, "driver-app-api").Annotations.OfType<EndpointAnnotation>().Should().Contain(endpoint => endpoint.IsExternal, "the Trip dispatch Logic App runs outside the Container Apps environment");
    }

    [Fact]
    public async Task InAzure_ThereIsNoLocalPartnerApi_ApiManagementIsTheOne()
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.Should().NotContain(resource => new[] { "partner-api", "trip-dispatch-worker", "driver-reports-worker", "tracking-worker", "proof-of-delivery-worker" }.Contains(resource.Name));
    }

    [Fact]
    public async Task InAzure_TheContainerAppsEnvironment_LogsToTheEstatesWorkspace()
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.OfType<AzureLogAnalyticsWorkspaceResource>().Should().ContainSingle()
            .Which.IsExisting().Should().BeTrue("retention is set once, in the estate (integrationRetentionDays)");
    }

    [Fact]
    public async Task InAzure_TheMigrations_RunAsAContainerAppsJob_NotAtStartup()
    {
        await using var builder = await CreateForAzureAsync();

        Resource(builder, "tms-migrations").Annotations.Should().Contain(annotation => annotation is AzureContainerAppJobCustomizationAnnotation);
        (await EnvironmentOfAsync(builder, "tms-api")).Keys.Should().NotContain("Database__MigrateOnStartup");
    }

    [Fact]
    public async Task InAzure_TheTmsApi_IsReachableFromOutsideTheContainerAppsEnvironment()
    {
        await using var builder = await CreateForAzureAsync();

        // The intake Logic App runs outside the environment.
        IsReachableFromOutsideTheContainerAppsEnvironment(builder, "tms-api").Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(DemoUis))]
    public async Task InAzure_ADemoUi_IsReachableFromThePresentersBrowser(string demoUi)
    {
        await using var builder = await CreateForAzureAsync();

        IsReachableFromOutsideTheContainerAppsEnvironment(builder, demoUi).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(DemoUis))]
    public async Task InAzure_ADemoUi_IsBehindAnEntraSignIn_ForTheDemoUisAppRegistration(string demoUi)
    {
        await using var builder = await CreateForAzureAsync();

        var bicep = ContainerAppBicepOf(builder, demoUi);

        bicep.Should().Contain("Microsoft.App/containerApps/authConfigs@")
            .And.MatchRegex(@"platform: \{\s*enabled: true\s*\}")
            .And.Contain("unauthenticatedClientAction: 'RedirectToLoginPage'")
            .And.Contain("redirectToProvider: 'azureactivedirectory'")
            .And.Contain("clientId: estate_demo_uis_client_id")
            .And.Contain("openIdIssuer: estate_entra_issuer");
        bicep.Should().NotContain("clientSecret", "Entra gives Easy Auth an ID token without a secret");
    }

    [Theory]
    [MemberData(nameof(DemoUis))]
    public async Task InAzure_ADemoUisHealthProbes_NeedNoSignIn(string demoUi)
    {
        await using var builder = await CreateForAzureAsync();

        var bicep = ContainerAppBicepOf(builder, demoUi);

        bicep.Should().MatchRegex(@"excludedPaths: \[\s*'/health'\s*'/alive'(\s*'/(carrier|shipper)/v1/[^']+/(bookings|notifications|cancellations)')*(\s*'/reset/v1')?\s*\]");
    }

    [Fact]
    public async Task InAzure_TheTmsUi_LeavesNothingButItsHealthProbesOutsideTheGate()
    {
        await using var builder = await CreateForAzureAsync();

        ContainerAppBicepOf(builder, "tms-web").Should().MatchRegex(@"excludedPaths: \[\s*'/health'\s*'/alive'\s*\]");
    }

    [Fact]
    public async Task InAzure_ThePartnerPortal_AlsoLeavesEachPersonasEndpointOutsideTheGate()
    {
        // The P4 booking, P7 Shipper notification and P7 Carrier cancellation Logic Apps are no browsers, and each endpoint asks its caller for the party's own credential (ADR 0024). So is the Simulator, whose reset endpoint asks for its key (ADR 0039).
        await using var builder = await CreateForAzureAsync();

        ContainerAppBicepOf(builder, "partner-portal-web").Should()
            .MatchRegex(@"excludedPaths: \[\s*'/health'\s*'/alive'\s*'/shipper/v1/SHP-0001/notifications'\s*'/shipper/v1/SHP-0003/notifications'\s*'/carrier/v1/CAR-0001/bookings'\s*'/carrier/v1/CAR-0002/bookings'\s*'/carrier/v1/CAR-0003/bookings'\s*'/carrier/v1/CAR-0001/cancellations'\s*'/carrier/v1/CAR-0002/cancellations'\s*'/carrier/v1/CAR-0003/cancellations'\s*'/reset/v1'\s*\]");
    }

    [Theory]
    [MemberData(nameof(DemoUis))]
    public async Task Locally_ADemoUi_HasNoSignIn(string demoUi)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        Resource(builder, demoUi).Annotations.Should().NotContain(annotation => annotation is AzureContainerAppCustomizationAnnotation);
        builder.Resources.OfType<ParameterResource>().Should().NotContain(parameter => parameter.Name == "estate-demo-uis-client-id");
    }

    [Fact]
    public async Task InAzure_TheDriverAppsScreens_AreBehindAnEntraSignIn_ForTheDriverAppsAppRegistration()
    {
        await using var builder = await CreateForAzureAsync();

        IsReachableFromOutsideTheContainerAppsEnvironment(builder, "driver-app-web").Should().BeTrue("the Driver's phone is outside the Container Apps environment");
        var bicep = ContainerAppBicepOf(builder, "driver-app-web");
        bicep.Should().Contain("Microsoft.App/containerApps/authConfigs@")
            .And.Contain("unauthenticatedClientAction: 'RedirectToLoginPage'")
            .And.Contain("clientId: estate_driver_app_registration_client_id")
            .And.Contain("openIdIssuer: estate_entra_issuer")
            .And.MatchRegex(@"excludedPaths: \[\s*'/health'\s*'/alive'\s*\]");
        bicep.Should().NotContain("clientSecret", "Entra gives Easy Auth an ID token without a secret");
        (await EnvironmentOfAsync(builder, "driver-app-web")).Keys.Should().NotContain(key => key.StartsWith("LocalSignIn__", StringComparison.Ordinal), "only Entra signs Drivers in in Azure");
    }

    [Fact]
    public async Task Locally_TheDriverApp_AndTheDriverReportsWorker_ShareTheTransitContainerOnAzurite()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        (await EnvironmentOfAsync(builder, "driver-app-web")).Keys.Should().Contain("ConnectionStrings__driver-app-blobs");
        (await EnvironmentOfAsync(builder, "driver-reports-worker")).Keys.Should().Contain("ConnectionStrings__driver-app-blobs");
        (await EnvironmentOfAsync(builder, "driver-app-api")).Keys.Should().Contain("ConnectionStrings__driver-app-blobs", "its mediator holds the Deliver handler, which needs the port");
        Resource(builder, "driver-app-web").Annotations.OfType<WaitAnnotation>().Should().Contain(wait => wait.Resource.Name == "blobs");
    }

    [Fact]
    public async Task InAzure_TheDriverAppsScreens_StoreTheFilesInTheEstatesTransitContainer_AsTheIdentityTheEstateLetsWriteThere()
    {
        await using var builder = await CreateForAzureAsync();

        (await EnvironmentOfAsync(builder, "driver-app-web")).Should().Contain("ConnectionStrings__driver-app-blobs", "{estate-driver-app-blob-endpoint.value}");
        (await EnvironmentOfAsync(builder, "driver-app-api")).Should().Contain("ConnectionStrings__driver-app-blobs", "{estate-driver-app-blob-endpoint.value}");
        Resource(builder, "driver-app-web").Annotations.OfType<AppIdentityAnnotation>()
            .Should().ContainSingle().Which.IdentityResource.Should().BeOfType<AzureUserAssignedIdentityResource>().Which.Name.Should().Be("driver-app-identity");
    }

    [Fact]
    public async Task InAzure_ThePartnerPortal_ShowsThePresenterTheDriversEntraTestUsers()
    {
        await using var builder = await CreateForAzureAsync();

        var portal = await EnvironmentOfAsync(builder, "partner-portal-web");

        portal.Should().Contain("DriverSignIns__Domain", "{estate-driver-sign-in-domain.value}")
            .And.Contain("DriverSignIns__Password", "{estate-driver-sign-in-password.value}");
    }

    [Fact]
    public async Task Locally_TheDriverApp_SignsDriversInWithAStandInForEntra()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        (await EnvironmentOfAsync(builder, "driver-app-web")).Should().Contain("LocalSignIn__Enabled", "true");
        Resource(builder, "driver-app-web").Annotations.Should().NotContain(annotation => annotation is AzureContainerAppCustomizationAnnotation);
        builder.Resources.OfType<ParameterResource>().Should().NotContain(parameter => parameter.Name.StartsWith("estate-driver-", StringComparison.Ordinal) && parameter.Name != "estate-driver-app-identity-name" && parameter.Name != "estate-driver-app-key-vault" && parameter.Name != "estate-driver-app-blob-endpoint");
        (await EnvironmentOfAsync(builder, "partner-portal-web")).Keys.Should().NotContain(key => key.StartsWith("DriverSignIns__", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InAzure_TheIntegrationConsole_SearchesTheEstatesWorkspace_AsItsOwnIdentity_WithinTheEstatesRetention()
    {
        await using var builder = await CreateForAzureAsync();

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Should().Contain("Estate__LogAnalyticsWorkspaceId", "{estate-log-analytics-workspace-id.value}")
            .And.Contain("Estate__IntegrationRetentionDays", "{estate-integration-retention-days.value}");
        Resource(builder, "integration-console-web").Annotations.OfType<AppIdentityAnnotation>()
            .Should().ContainSingle().Which.IdentityResource.Should().BeOfType<AzureUserAssignedIdentityResource>().Which.Name.Should().Be("integration-console-identity");
    }

    [Fact]
    public async Task InAzure_TheIntegrationConsole_ReadsRunsFromTheEstatesLogicApps()
    {
        await using var builder = await CreateForAzureAsync();

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Should().Contain("Estate__ResourceGroupId", "{estate-resource-group-id.value}");
    }

    [Fact]
    public async Task Locally_TheIntegrationConsole_GivenADeployedEstate_ReadsRunsFromItsLogicApps()
    {
        // Logic Apps exist only in Azure, so a run opens only once the developer names the estate's resource group.
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            ["--Parameters:estate-resource-group-id=/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia"],
            TestContext.Current.CancellationToken);

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Should().Contain("Estate__ResourceGroupId", "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia");
    }

    [Fact]
    public async Task Locally_TheIntegrationConsole_StartsWithoutAnEstate()
    {
        // It reads a deployed estate once the developer gives it the estate's parameters; until then search says so.
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Should().Contain("Estate__LogAnalyticsWorkspaceId", string.Empty)
            .And.Contain("Estate__IntegrationRetentionDays", "90")
            .And.Contain("Estate__ResourceGroupId", string.Empty);
        builder.Resources.OfType<ParameterResource>().Should().NotContain(parameter => parameter.Name == "estate-log-analytics-workspace-id" || parameter.Name == "estate-resource-group-id");
    }

    [Fact]
    public async Task Locally_TheIntegrationConsole_GivenADeployedEstate_SearchesItsWorkspaceWithinItsRetention()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            ["--Parameters:estate-log-analytics-workspace-id=7f4c1b0e-0000-0000-0000-000000000001", "--Parameters:estate-integration-retention-days=30"],
            TestContext.Current.CancellationToken);

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Should().Contain("Estate__LogAnalyticsWorkspaceId", "7f4c1b0e-0000-0000-0000-000000000001")
            .And.Contain("Estate__IntegrationRetentionDays", "30");
    }

    [Fact]
    public async Task Locally_TheIntegrationConsole_WithoutAnEstate_SearchesTheAspireDashboardsTelemetry()
    {
        // The dashboard listens where the AppHost's launch profile says, and its telemetry API takes the AppHost's key.
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            ["--ASPNETCORE_URLS=https://localhost:17180;http://localhost:15180", "--AppHost:DashboardApiKey=dashboard-api-key"],
            TestContext.Current.CancellationToken);

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Should().Contain("LocalTelemetry__DashboardUrl", "http://localhost:15180")
            .And.Contain("LocalTelemetry__ApiKey", "dashboard-api-key");
    }

    [Fact]
    public async Task Locally_TheIntegrationConsole_GivenADeployedEstate_DoesNotSearchTheDashboard()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [
                "--ASPNETCORE_URLS=http://localhost:15180", "--AppHost:DashboardApiKey=dashboard-api-key",
                "--Parameters:estate-log-analytics-workspace-id=7f4c1b0e-0000-0000-0000-000000000001", "--Parameters:estate-integration-retention-days=30",
            ],
            TestContext.Current.CancellationToken);

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Keys.Should().NotContain(key => key.StartsWith("LocalTelemetry__", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InAzure_TheIntegrationConsole_DoesNotSearchADashboard()
    {
        await using var builder = await CreateForAzureAsync();

        var console = await EnvironmentOfAsync(builder, "integration-console-web");

        console.Keys.Should().NotContain(key => key.StartsWith("LocalTelemetry__", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Locally_NoApp_SendsTelemetryToApplicationInsights_SoLocalTrailsNeverMixWithTheEstates()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        foreach (var app in builder.Resources.OfType<ProjectResource>())
        {
            (await EnvironmentOfAsync(builder, app.Name)).Keys.Should().NotContain("APPLICATIONINSIGHTS_CONNECTION_STRING", app.Name);
        }
    }

    [Fact]
    public async Task Locally_TheIntegrationConsole_GivenAWorkspaceWithoutItsRetention_DoesNotStart()
    {
        // Guessing the retention would cap searches at a period the estate doesn't keep.
        var create = () => DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            ["--Parameters:estate-log-analytics-workspace-id=7f4c1b0e-0000-0000-0000-000000000001"],
            TestContext.Current.CancellationToken);

        (await create.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("Parameters:estate-integration-retention-days");
    }

    private static Task<IDistributedApplicationTestingBuilder> CreateForAzureAsync() =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            ["--operation", "publish", "--publisher", "default", "--output-path", Path.GetTempPath()],
            TestContext.Current.CancellationToken);

    private static IResource Resource(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.Single(resource => resource.Name == name);

    private static bool IsReachableFromOutsideTheContainerAppsEnvironment(IDistributedApplicationTestingBuilder builder, string name) =>
        Resource(builder, name).Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.IsExternal);

    /// <summary>What the AppHost adds to the Bicep of the container app azd deploys for a resource.</summary>
    private static string ContainerAppBicepOf(IDistributedApplicationTestingBuilder builder, string name)
    {
        var customizations = Resource(builder, name).Annotations.OfType<AzureContainerAppCustomizationAnnotation>().ToList();
        var containerApp = new AzureProvisioningResource(name, infrastructure =>
        {
            var app = new ContainerApp("app");
            infrastructure.Add(app);
            customizations.ForEach(customization => customization.Configure(infrastructure, app));
        });
        return containerApp.GetBicepTemplateString();
    }

    /// <summary>The environment a resource gets as azd deploys it, with each value as its manifest expression.</summary>
    private static async Task<Dictionary<string, string>> EnvironmentOfAsync(IDistributedApplicationTestingBuilder builder, string name)
    {
        var resource = Resource(builder, name);
        // Some callbacks, such as a reference's, ask which resource they configure.
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(
            variable => variable.Key,
            variable => variable.Value switch
            {
                IManifestExpressionProvider expression => expression.ValueExpression,
                _ => variable.Value.ToString() ?? string.Empty,
            });
    }
}
