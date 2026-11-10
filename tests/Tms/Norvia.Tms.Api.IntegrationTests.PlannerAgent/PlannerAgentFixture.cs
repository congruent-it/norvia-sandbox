using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>The TMS with the Agents switched on (<see cref="AgentMcpFixture"/>), with the Planner agent's tool served at <c>/mcp/planner</c>.</summary>
public sealed class PlannerAgentFixture : AgentMcpFixture
{
    public const string McpPath = "/mcp/planner";

    /// <summary>A token as Entra issues it to the Planner agent's identity for the MCP server; change any part to show what is refused.</summary>
    public string AgentToken(string? agentId = null, string[]? roles = null, string? signingKey = null) =>
        IssueToken(roles ?? [LocalTokens.PlannerAgentReadRole], LocalTokens.PlannerAgentAudience, agentId ?? NewAgentId(), signingKey);

    /// <summary>A token as Entra issues it to the Customer service agent's identity, which is not for the Planner agent's server.</summary>
    public string CustomerServiceAgentToken() =>
        IssueToken([LocalTokens.CustomerServiceAgentReadRole], LocalTokens.CustomerServiceAgentAudience, NewAgentId());

    /// <summary>The MCP endpoint a runner connects to.</summary>
    public Uri McpAddress => McpEndpoint(McpPath);
}
