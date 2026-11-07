using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Norvia.Erp.Application.Agents;

namespace Norvia.Erp.Infrastructure.Agents;

public static class AgentsInfrastructure
{
    /// <summary>
    /// Registers the ERP UI's <see cref="IAgentClient"/>. Off unless <c>Agents:Enabled</c> is on, as ADR 0040 has it. On, the local runner
    /// calls the MCP stand-in (ADR 0041). The Foundry client comes with the Azure slice.
    /// </summary>
    public static IHostApplicationBuilder AddAgentClient(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<AgentsOptions>().BindConfiguration(AgentsOptions.SectionName);
        if (!builder.Configuration.GetValue<bool>($"{AgentClientOptions.SectionName}:{nameof(AgentClientOptions.Enabled)}"))
        {
            builder.Services.AddSingleton<IAgentClient, DisabledAgentClient>();
            return builder;
        }

        builder.Services.AddOptions<AgentClientOptions>().BindConfiguration(AgentClientOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.McpAddress is { IsAbsoluteUri: true }, "Agents:McpAddress must be the MCP server's absolute address.")
            .Validate(options => options.Tokens.IsComplete, "Agents:Tokens needs an Issuer, an Audience, a ClientId, a Role and a SigningKey.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IRunIds, GuidRunIds>();
        builder.Services.TryAddSingleton<AgentActivities>();
        builder.Services.AddTransient<BearerTokenHandler>();
        // A short timeout and no retry: the client has its own deadline, and the standard resilience handler would retry (ADR 0040).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is the only way to opt a client out of the defaults.
        builder.Services.AddHttpClient<IAgentClient, LocalAgentClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<BearerTokenHandler>()
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        return builder;
    }
}
