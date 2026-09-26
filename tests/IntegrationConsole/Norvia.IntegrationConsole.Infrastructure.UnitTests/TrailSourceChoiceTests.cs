using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests;

/// <summary>
/// The AppHost chooses where the console searches: the Aspire dashboard locally, the estate's Log Analytics workspace
/// in Azure or when a developer points the console at a deployed estate (ADR 0027).
/// </summary>
public sealed class TrailSourceChoiceTests
{
    [Fact]
    public void AddIntegrationConsoleInfrastructure_GivenTheDashboard_SearchesTheDashboard()
    {
        var source = TrailSourceWith(new()
        {
            ["LocalTelemetry:DashboardUrl"] = "http://localhost:15180",
            ["LocalTelemetry:ApiKey"] = "dashboard-api-key",
        });

        source.Should().BeOfType<DashboardTrailSource>();
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_GivenAnEstate_SearchesItsLogAnalyticsWorkspace()
    {
        var source = TrailSourceWith(new() { ["Estate:LogAnalyticsWorkspaceId"] = "00000000-0000-0000-0000-000000000002" });

        source.Should().BeOfType<LogAnalyticsTrailSource>();
    }

    private static ITrailSource TrailSourceWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddIntegrationConsoleInfrastructure();

        using var services = builder.Services.BuildServiceProvider();
        return services.GetRequiredService<ITrailSource>();
    }
}
