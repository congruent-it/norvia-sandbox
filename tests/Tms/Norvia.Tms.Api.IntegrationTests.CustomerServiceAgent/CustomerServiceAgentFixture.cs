using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The TMS with the Agents switched on (<see cref="AgentMcpFixture"/>), with the Customer service agent's tool served at <c>/mcp/customer-service</c> and the reply agent's at
/// <c>/mcp/customer-service-reply</c>.
/// </summary>
public sealed class CustomerServiceAgentFixture : AgentMcpFixture
{
    public const string McpPath = "/mcp/customer-service";
    public const string ReplyMcpPath = "/mcp/customer-service-reply";

    /// <summary>A token as Entra issues it to the Customer service agent's identity for the MCP server; change any part to show what is refused.</summary>
    public string AgentToken(string? agentId = null, string[]? roles = null, string? signingKey = null) =>
        IssueToken(roles ?? [LocalTokens.CustomerServiceAgentReadRole], LocalTokens.CustomerServiceAgentAudience, agentId ?? NewAgentId(), signingKey);

    /// <summary>A token as Entra issues it to the Customer service reply agent's identity for its MCP server; change any part to show what is refused.</summary>
    public string ReplyAgentToken(string? agentId = null, string[]? roles = null) =>
        IssueToken(roles ?? [LocalTokens.CustomerServiceReplyAgentReadRole], LocalTokens.CustomerServiceReplyAgentAudience, agentId ?? NewAgentId());

    /// <summary>A token for the TMS API's agent view itself, as the stand-in signs it for the Agent's identity: the TMS API's audience, with the roles given.</summary>
    public string AgentViewToken(string[] roles, string? agentId = null) => IssueToken(roles, LocalTokens.TmsAudience, agentId ?? NewAgentId());

    /// <summary>The MCP endpoint a runner connects to.</summary>
    public Uri McpAddress => McpEndpoint(McpPath);

    /// <summary>The MCP endpoint the Customer service reply agent's runner connects to.</summary>
    public Uri ReplyMcpAddress => McpEndpoint(ReplyMcpPath);
}
