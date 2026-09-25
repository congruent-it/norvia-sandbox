using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Provisioning.AppContainers;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>Checks the AppHost's resource model without starting anything, as it runs locally and as azd deploys it.</summary>
public sealed class AppHostModelTests
{
    [Fact]
    public async Task EveryTopic_HasASubscription_SoTheServiceBusEmulatorStarts()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var subscribedTopics = builder.Resources.OfType<AzureServiceBusSubscriptionResource>().Select(s => s.Parent).ToHashSet();

        builder.Resources.OfType<AzureServiceBusTopicResource>().Should().OnlyContain(topic => subscribedTopics.Contains(topic));
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
        ((IResourceWithEndpoints)Resource(builder, "tms-api")).Annotations.OfType<EndpointAnnotation>()
            .Should().Contain(endpoint => endpoint.IsExternal);
    }

    [Theory]
    [InlineData("tms-web")]
    [InlineData("partner-portal-web")]
    public async Task InAzure_ADemoUi_IsReachableFromThePresentersBrowser(string demoUi)
    {
        await using var builder = await CreateForAzureAsync();

        ((IResourceWithEndpoints)Resource(builder, demoUi)).Annotations.OfType<EndpointAnnotation>()
            .Should().Contain(endpoint => endpoint.IsExternal);
    }

    [Theory]
    [InlineData("tms-web")]
    [InlineData("partner-portal-web")]
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
    [InlineData("tms-web")]
    [InlineData("partner-portal-web")]
    public async Task InAzure_ADemoUisHealthProbes_NeedNoSignIn(string demoUi)
    {
        await using var builder = await CreateForAzureAsync();

        var bicep = ContainerAppBicepOf(builder, demoUi);

        bicep.Should().MatchRegex(@"excludedPaths: \[\s*'/health'\s*'/alive'\s*\]");
    }

    [Theory]
    [InlineData("tms-web")]
    [InlineData("partner-portal-web")]
    public async Task Locally_ADemoUi_HasNoSignIn(string demoUi)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        Resource(builder, demoUi).Annotations.Should().NotContain(annotation => annotation is AzureContainerAppCustomizationAnnotation);
        builder.Resources.OfType<ParameterResource>().Should().NotContain(parameter => parameter.Name == "estate-demo-uis-client-id");
    }

    private static Task<IDistributedApplicationTestingBuilder> CreateForAzureAsync() =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            ["--operation", "publish", "--publisher", "default", "--output-path", Path.GetTempPath()],
            TestContext.Current.CancellationToken);

    private static IResource Resource(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.Single(resource => resource.Name == name);

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
