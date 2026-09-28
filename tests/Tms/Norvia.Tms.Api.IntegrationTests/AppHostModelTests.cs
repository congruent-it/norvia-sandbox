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
            .And.Contain("PartnerApi__PersonaClientIds", "brouwer-dranken=norvia-local-persona-brouwer-dranken;van-dijk-transport=norvia-local-persona-van-dijk-transport")
            .And.Contain("PartnerApi__LocalTokens__Roles__brouwer-dranken__0", "TransportOrders.Submit")
            .And.ContainKey("PartnerApi__LocalTokens__SigningKey");
        portal.Keys.Should().NotContain("PartnerApi__LocalTokens__Roles__van-dijk-transport__0", "a Carrier Persona holds no submit role");
        portal.Keys.Should().NotContain(key => key.StartsWith("PartnerApi__Entra", StringComparison.Ordinal));
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

        foreach (var persona in new[] { "brouwer-dranken", "van-dijk-transport" })
        {
            var key = $"PartnerApi__SubscriptionKeys__{persona}";
            portal[key].Should().NotBeNullOrEmpty().And.Be(partnerApi[key]);
        }

        partnerApi.Should().Contain("PartnerApi__ShippersByClientId__norvia-local-persona-brouwer-dranken", "SHP-0001");
        partnerApi.Keys.Should().NotContain(key => key.Contains("van-dijk-transport", StringComparison.Ordinal) && key.Contains("ShippersByClientId", StringComparison.Ordinal));
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

        Regex.Count(bicep, "useFreeLimit: true").Should().Be(2, "both databases stay on the free offer");
        Regex.Count(bicep, "freeLimitExhaustionBehavior: 'BillOverUsage'").Should().Be(2, "a paused database fails the migrations and the demo");
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

        builder.Resources.OfType<AzureKeyVaultResource>().Single().IsExisting().Should().BeTrue();
        foreach (var persona in new[] { "brouwer-dranken", "van-dijk-transport" })
        {
            portal[$"PartnerApi__SubscriptionKeys__{persona}"].Should().Contain($"subscription-key-{persona}");
        }
    }

    [Fact]
    public async Task InAzure_ThereIsNoLocalPartnerApi_ApiManagementIsTheOne()
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.Should().NotContain(resource => resource.Name == "partner-api");
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

        bicep.Should().MatchRegex(@"excludedPaths: \[\s*'/health'\s*'/alive'\s*\]");
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
