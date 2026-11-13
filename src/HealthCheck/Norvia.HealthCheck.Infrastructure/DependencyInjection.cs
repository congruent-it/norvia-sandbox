using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Estate;
using Norvia.HealthCheck.Infrastructure.Cost;
using Norvia.HealthCheck.Infrastructure.Agents;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The client ID of the app's user-assigned managed identity, which the estate Bicep owns. Aspire sets it on the container app it
    /// gives that identity.
    /// </summary>
    public const string ManagedIdentityClientIdKey = "AZURE_CLIENT_ID";

    public static IHostApplicationBuilder AddHealthCheckInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(AppCredential(builder.Configuration));
        if (builder.Configuration.GetSection(AzureEstateOptions.SectionName)[nameof(AzureEstateOptions.ResourceGroupId)] is { Length: > 0 })
        {
            // In Azure the estate's configuration is read from the real estate, as the app's own identity, which holds Reader on its resource group.
            builder.Services.AddOptions<AzureEstateOptions>().BindConfiguration(AzureEstateOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
            builder.Services.AddHttpClient(AzureEstateConfiguration.ManagementClient);
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IEstateConfiguration, AzureEstateConfiguration>();
            builder.Services.AddHostedService<ManagementTokenWarmUp>();

            // The cost is Cost Management's, read as the same identity (Cost Management Reader on the resource group), and the Transport Orders
            // per day are the TMS API's, which grants that identity its volume role only.
            builder.Services.AddOptions<TmsApiOptions>().BindConfiguration(TmsApiOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
            builder.Services.AddHttpClient<ITransportOrderVolume, TmsTransportOrderVolume>((services, http) =>
                http.BaseAddress = services.GetRequiredService<IOptions<TmsApiOptions>>().Value.BaseAddress);
            builder.Services.AddTransient<ICostReader, CostManagementCostReader>();
        }
        else
        {
            // Locally nothing reads a running estate: the configuration is a snapshot.
            builder.Services.AddOptions<SnapshotOptions>().BindConfiguration(SnapshotOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
            builder.Services.AddSingleton<IEstateConfiguration, SnapshotEstateConfiguration>();
            builder.Services.AddOptions<CostSnapshotOptions>().BindConfiguration(CostSnapshotOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
            builder.Services.AddSingleton<ICostReader, SnapshotCostReader>();
        }

        builder.AddAgentClient();
        return builder;
    }

    /// <summary>
    /// What the adapters call Azure as. In Azure the app is its own managed identity: the one holding Reader on the estate's resource group, and
    /// the one that asks the Agent. Locally, where a developer points it at a deployed estate, that is the developer's own sign-in.
    /// </summary>
    private static TokenCredential AppCredential(IConfiguration configuration) =>
        configuration[ManagedIdentityClientIdKey] is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
            });
}
