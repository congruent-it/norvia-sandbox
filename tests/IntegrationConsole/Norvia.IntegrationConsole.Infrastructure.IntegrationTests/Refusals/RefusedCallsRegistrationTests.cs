using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.IntegrationConsole.Application.Refusals;
using Norvia.IntegrationConsole.Infrastructure.Refusals;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests.Refusals;

/// <summary>Which source the console lists refused calls from, and as whom it reads it, as the console is configured in the estate and locally.</summary>
public sealed class RefusedCallsRegistrationTests
{
    private const string ManagedIdentityClientId = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public void InTheEstate_TheConsoleListsRefusalsFromApplicationInsights_AsItsManagedIdentity()
    {
        using var services = ConsoleServices(new()
        {
            ["AZURE_CLIENT_ID"] = ManagedIdentityClientId,
            ["Estate:LogAnalyticsWorkspaceId"] = "7f4c1b0e-0000-0000-0000-000000000001",
        });

        services.GetRequiredService<IRefusedCalls>().Should().BeOfType<LogAnalyticsRefusedCalls>();
        // The adapter's only identity is the console's credential; it is given no operator and no token.
        services.GetRequiredService<TokenCredential>().Should().BeOfType<ManagedIdentityCredential>();
    }

    [Fact]
    public void Locally_TheConsoleListsRefusalsFromTheAspireDashboard()
    {
        using var services = ConsoleServices(new()
        {
            ["LocalTelemetry:DashboardUrl"] = "http://localhost:15180",
            ["LocalTelemetry:ApiKey"] = "dashboard-api-key",
        });

        services.GetRequiredService<IRefusedCalls>().Should().BeOfType<DashboardTrailSource>();
    }

    private static ServiceProvider ConsoleServices(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddIntegrationConsoleInfrastructure();
        return builder.Services.BuildServiceProvider();
    }
}
