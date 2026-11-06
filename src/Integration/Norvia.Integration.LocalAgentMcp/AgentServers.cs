using Microsoft.Extensions.Options;
using Norvia.Agents.Tools;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp;

/// <summary>
/// The MCP servers the stand-in plays: the Integration operations agent's, at <c>/mcp</c>, and the Estate health check's, at
/// <c>/mcp/health-check</c>, and the Customer service agent's, at <c>/mcp/customer-service</c>. Each has its tool list (the contract file the Bicep reads too), the tokens it accepts from its Agent, the agent
/// API it calls and the tokens it calls it with, as API Management has one server per application's agent view (ADR 0040).
/// </summary>
internal sealed class AgentServers(IEnumerable<ToolListContract> contracts, IOptions<AgentMcpOptions> options)
{
    /// <summary>The server <c>/mcp</c> without a name has been since the first Agent.</summary>
    public const string Default = "integration-operations";

    public const string HealthCheck = "health-check";

    public const string CustomerService = "customer-service";

    /// <summary>The server, or null when the stand-in doesn't serve one by that name (or it isn't configured).</summary>
    public AgentServer? Find(string? name)
    {
        var server = string.IsNullOrEmpty(name) ? Default : name;
        var contract = contracts.FirstOrDefault(contract => string.Equals(contract.Server, server, StringComparison.Ordinal));
        var settings = options.Value;
        return contract is null
            ? null
            : server switch
            {
                Default when settings.ConsoleAgentApi is { } api => new AgentServer(contract, settings.Tokens, api, settings.ConsoleTokens),
                HealthCheck when settings.HealthCheck is { Api: { } api } health => new AgentServer(contract, health.Tokens, api, health.ApiTokens),
                CustomerService when settings.CustomerService is { Api: { } api } customerService => new AgentServer(contract, customerService.Tokens, api, customerService.ApiTokens),
                _ => null,
            };
    }
}

/// <param name="AgentTokens">What the Agent's token must satisfy.</param>
/// <param name="Api">The application's agent API, which the tools are read from.</param>
/// <param name="ApiTokens">What the stand-in signs its token for the agent API with, as the Agent's identity.</param>
internal sealed record AgentServer(ToolListContract Contract, LocalTokenOptions AgentTokens, Uri Api, LocalTokenOptions ApiTokens)
{
    /// <summary>The <c>flow</c> of the server's traces, named as the API Management policy names it.</summary>
    public string Flow => $"agent-mcp-{Contract.Server}";
}
