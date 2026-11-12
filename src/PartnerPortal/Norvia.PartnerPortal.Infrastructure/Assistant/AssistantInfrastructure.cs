using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Infrastructure.Assistant;

public static class AssistantInfrastructure
{
    /// <summary>
    /// Registers the Shipper tracking assistant (ADR 0049). Off unless <c>Agents:Enabled</c> is on. The limits and their counters are in the portal's
    /// memory, so the portal runs one replica while the assistant is on.
    /// </summary>
    public static IHostApplicationBuilder AddTrackingAssistant(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<AssistantOptions>().BindConfiguration(AssistantOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<AssistantOptions>>().Value.Limits);
        builder.Services.AddSingleton<AssistantLimiter>();
        builder.Services.AddSingleton<IAssistantSwitch, ConfiguredAssistantSwitch>();
        builder.Services.AddSingleton<IAssistantRunIds, GuidAssistantRunIds>();
        builder.Services.AddSingleton(_ => new AssistantActivities(builder.Environment.ApplicationName));
        builder.Services.AddSingleton<IAgentClient>(services =>
            services.GetRequiredService<IOptions<AssistantOptions>>().Value.Enabled
                ? ActivatorUtilities.CreateInstance<LocalAgentClient>(services)
                : new DisabledAgentClient());
        return builder;
    }
}
