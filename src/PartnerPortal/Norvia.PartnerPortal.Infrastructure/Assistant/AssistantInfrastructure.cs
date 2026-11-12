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
            .Validate(options => options.TimeoutSeconds > 0, "Agents:TimeoutSeconds must be at least 1.")
            .Validate(options => options.Limits.QuestionsPerMinute > 0 && options.Limits.QuestionsPerHour > 0 && options.Limits.MaxQuestionCharacters > 0
                && options.Limits.MaxOrders > 0 && options.Limits.MaxEventsPerOrder > 0 && options.Limits.MaxOutputTokens > 0
                && options.Limits.DailyTokensPerPersona > 0 && options.Limits.DailyTokensEstate > 0, "Every Agents:Limits value must be at least 1.")
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
