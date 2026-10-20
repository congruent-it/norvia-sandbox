using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Runs;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Trails;

/// <summary>
/// The AppHost chooses where the console searches: the Aspire dashboard locally, the estate's Log Analytics workspace
/// in Azure or when a developer points the console at a deployed estate (ADR 0027). Settings that can't be right stop
/// the console at startup (coding guidelines §8).
/// </summary>
public sealed class TrailSourceRegistrationTests
{
    [Fact]
    public void AddIntegrationConsoleInfrastructure_SearchesTrailsInTheEstatesWorkspace()
    {
        using var services = ServicesWith(new() { ["Estate:LogAnalyticsWorkspaceId"] = "7f4c1b0e-0000-0000-0000-000000000001" });

        services.GetRequiredService<ITrailSource>().Should().BeOfType<LogAnalyticsTrailSource>();
        services.GetRequiredService<IOptions<TrailSourceOptions>>().Value.LogAnalyticsWorkspaceId.Should().Be("7f4c1b0e-0000-0000-0000-000000000001");
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithoutAnEstate_StillStarts()
    {
        // Locally the console runs before a developer points it at a deployed estate; search then says so.
        using var services = ServicesWith([]);

        var start = () => Start(services);

        start.Should().NotThrow();
        services.GetRequiredService<ITrailSource>().Should().NotBeNull();
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithAWorkspaceIdThatIsNoGuid_DoesNotStart()
    {
        using var services = ServicesWith(new() { ["Estate:LogAnalyticsWorkspaceId"] = "norvia-logs" });

        var start = () => Start(services);

        start.Should().Throw<OptionsValidationException>().WithMessage("*Estate:LogAnalyticsWorkspaceId*");
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_GivenTheDashboard_SearchesTheDashboard()
    {
        using var services = ServicesWith(new()
        {
            ["LocalTelemetry:DashboardUrl"] = "http://localhost:15180",
            ["LocalTelemetry:ApiKey"] = "dashboard-api-key",
        });

        var start = () => Start(services);

        start.Should().NotThrow();
        services.GetRequiredService<ITrailSource>().Should().BeOfType<DashboardTrailSource>();
        // No Logic Apps run locally, so failed runs are the failures the dashboard holds.
        services.GetRequiredService<IFailedRuns>().Should().BeOfType<DashboardTrailSource>();
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_InAzure_ListsFailedRunsFromTheEstatesLogicApps()
    {
        using var services = ServicesWith(new() { ["Estate:ResourceGroupId"] = Runs.RunHistory.ResourceGroupId });

        services.GetRequiredService<IFailedRuns>().Should().BeOfType<LogicAppsWorkflowRuns>();
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_GivenADashboardUrlThatIsNotAbsolute_DoesNotStart()
    {
        using var services = ServicesWith(new() { ["LocalTelemetry:DashboardUrl"] = "localhost-15180" });

        var start = () => Start(services);

        start.Should().Throw<OptionsValidationException>().WithMessage("*LocalTelemetry:DashboardUrl*");
    }

    // What the host runs before it starts: every ValidateOnStart registration.
    private static void Start(ServiceProvider services) => services.GetRequiredService<IStartupValidator>().Validate();

    private static ServiceProvider ServicesWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddIntegrationConsoleInfrastructure();

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
