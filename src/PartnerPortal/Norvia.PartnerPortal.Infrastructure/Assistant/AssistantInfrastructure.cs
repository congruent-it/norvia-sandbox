using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
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
            .Validate(options => !options.UsesFoundry || options.Foundry.ProjectEndpoint is { IsAbsoluteUri: true }, "Agents:Foundry:ProjectEndpoint must be the Foundry project's absolute address.")
            .Validate(options => !options.Enabled || !options.UsesFoundry || !string.IsNullOrWhiteSpace(options.ManagedIdentityClientId), "Agents:ManagedIdentityClientId is needed to ask the Foundry project.")
            .ValidateOnStart();
        builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<AssistantOptions>>().Value.Limits);
        builder.Services.AddSingleton<AssistantLimiter>();
        builder.Services.AddSingleton<IAssistantSwitch, ConfiguredAssistantSwitch>();
        builder.Services.AddSingleton<IAssistantRunIds, GuidAssistantRunIds>();
        builder.Services.AddSingleton(_ => new AssistantActivities(builder.Environment.ApplicationName));
        // On, the Foundry client asks the Agent published in the project the estate named (Agents:Foundry:ProjectEndpoint); without one, the in-process
        // runner plays it with the scripted fake (ADR 0041). A short timeout and no retry: the client has its own deadline, and the standard
        // resilience handler would retry (ADR 0040).
        if (!builder.Configuration.GetValue<bool>($"{AssistantOptions.SectionName}:{nameof(AssistantOptions.Enabled)}"))
        {
            builder.Services.AddSingleton<IAgentClient, DisabledAgentClient>();
        }
        else if (!string.IsNullOrWhiteSpace(builder.Configuration[$"{AssistantOptions.SectionName}:Foundry:ProjectEndpoint"]))
        {
            builder.Services.AddSingleton<TokenCredential>(services => new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(services.GetRequiredService<IOptions<AssistantOptions>>().Value.ManagedIdentityClientId)));
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is the only way to opt a client out of the defaults.
            builder.Services.AddHttpClient<IAgentClient, FoundryAgentClient>(http => http.Timeout = Timeout.InfiniteTimeSpan).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        }
        else
        {
            builder.Services.AddSingleton<IAgentClient>(services => ActivatorUtilities.CreateInstance<LocalAgentClient>(services));
        }

        return builder;
    }
}
