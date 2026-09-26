using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests;

public sealed class ConsoleCredentialTests
{
    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithTheConsolesManagedIdentity_CallsAzureAsThatIdentity()
    {
        var credential = CredentialWith(new() { ["AZURE_CLIENT_ID"] = "00000000-0000-0000-0000-000000000001" });

        credential.Should().BeOfType<ManagedIdentityCredential>();
    }

    [Fact]
    public void AddIntegrationConsoleInfrastructure_WithoutAManagedIdentity_CallsAzureAsTheDeveloper()
    {
        // Locally the console reads a deployed estate with the developer's own sign-in (Azure CLI, Visual Studio...).
        var credential = CredentialWith([]);

        credential.Should().BeOfType<DefaultAzureCredential>();
    }

    private static TokenCredential CredentialWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddIntegrationConsoleInfrastructure();

        using var services = builder.Services.BuildServiceProvider();
        return services.GetRequiredService<TokenCredential>();
    }
}
