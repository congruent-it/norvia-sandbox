using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Agents;

namespace Norvia.IntegrationConsole.Infrastructure.Agents;

public static class AgentsInfrastructure
{
    /// <summary>
    /// Registers the console's <see cref="IAgentClient"/>. Off unless <c>Agents:Enabled</c> is on, as ADR 0040 has it. On, the Foundry
    /// client asks the Agent published in the project the estate named (<c>Agents:Foundry:ProjectEndpoint</c>); without one, the local
    /// runner calls the MCP stand-in (ADR 0041).
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
            .Validate(options => !options.UsesFoundry || options.Foundry.ProjectEndpoint is { IsAbsoluteUri: true }, "Agents:Foundry:ProjectEndpoint must be the Foundry project's absolute address.")
            .Validate(options => options.UsesFoundry || options.McpAddress is { IsAbsoluteUri: true }, "Agents:McpAddress must be the MCP server's absolute address.")
            .Validate(options => options.UsesFoundry || options.Tokens.IsComplete, "Agents:Tokens needs an Issuer, an Audience, a ClientId, a Role and a SigningKey.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IRunIds, GuidRunIds>();
        builder.Services.TryAddSingleton<AgentActivities>();
        // A short timeout and no retry: the client has its own deadline, and the standard resilience handler would retry (ADR 0040).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is the only way to opt a client out of the defaults.
        if (!string.IsNullOrWhiteSpace(builder.Configuration[$"{AgentClientOptions.SectionName}:Foundry:ProjectEndpoint"]))
        {
            builder.Services.AddHttpClient<IAgentClient, FoundryAgentClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
                .RemoveAllResilienceHandlers();
            return builder;
        }

        builder.Services.AddTransient<BearerTokenHandler>();
        builder.Services.AddHttpClient<IAgentClient, LocalAgentClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<BearerTokenHandler>()
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        return builder;
    }
}
