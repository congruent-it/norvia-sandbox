using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norvia.IntegrationConsole.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The client ID of the console's user-assigned managed identity, which the estate Bicep owns. Aspire sets it on the
    /// container app it gives that identity.
    /// </summary>
    public const string ManagedIdentityClientIdKey = "AZURE_CLIENT_ID";

    public static IHostApplicationBuilder AddIntegrationConsoleInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton(ConsoleCredential(builder.Configuration));

        return builder;
    }

    /// <summary>
    /// What the adapters call Azure as. The console checks the operator's app role, then reads the estate as its own
    /// managed identity, never with the operator's token. Locally, where it reads a deployed estate, that is the
    /// developer's own sign-in (Azure CLI, Visual Studio and the like).
    /// </summary>
    private static TokenCredential ConsoleCredential(IConfiguration configuration) =>
        configuration[ManagedIdentityClientIdKey] is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
            });
}
