using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;

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
    [InlineData("tms-api")]
    [InlineData("tms-web")]
    public async Task Locally_TheTms_StartsOnceItsDatabaseIsMigrated(string tmsResource)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        Resource(builder, tmsResource).Annotations.OfType<WaitAnnotation>()
            .Should().Contain(wait => wait.Resource.Name == "tms-migrations" && wait.WaitType == WaitType.WaitForCompletion);
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
    public async Task InAzure_TheTms_SendsItsTelemetryToTheEstatesApplicationInsights(string tmsResource)
    {
        await using var builder = await CreateForAzureAsync();

        builder.Resources.OfType<AzureApplicationInsightsResource>().Single().IsExisting().Should().BeTrue();
        (await EnvironmentOfAsync(builder, tmsResource)).Should().ContainKey("APPLICATIONINSIGHTS_CONNECTION_STRING");
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

    private static Task<IDistributedApplicationTestingBuilder> CreateForAzureAsync() =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            ["--operation", "publish", "--publisher", "default", "--output-path", Path.GetTempPath()],
            TestContext.Current.CancellationToken);

    private static IResource Resource(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.Single(resource => resource.Name == name);

    /// <summary>The environment a resource gets as azd deploys it, with each value as its manifest expression.</summary>
    private static async Task<Dictionary<string, string>> EnvironmentOfAsync(IDistributedApplicationTestingBuilder builder, string name)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in Resource(builder, name).Annotations.OfType<EnvironmentCallbackAnnotation>())
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
