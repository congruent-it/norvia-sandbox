using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.HealthCheck.Application.Estate;
using Norvia.HealthCheck.Infrastructure.Agents;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddHealthCheckInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Locally the estate's configuration is a snapshot; an adapter over Resource Graph and Azure Monitor replaces it in Azure (#392).
        builder.Services.AddOptions<SnapshotOptions>().BindConfiguration(SnapshotOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<IEstateConfiguration, SnapshotEstateConfiguration>();
        builder.AddAgentClient();
        return builder;
    }
}
