using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Trails;

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

        services.GetRequiredService<ITrailSource>().Should().NotBeNull();
    }

    private static ServiceProvider ServicesWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddIntegrationConsoleInfrastructure();

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
