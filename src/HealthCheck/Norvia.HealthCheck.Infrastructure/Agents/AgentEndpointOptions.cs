namespace Norvia.HealthCheck.Infrastructure.Agents;

/// <summary>An Agent's own MCP server and token, locally.</summary>
public sealed class AgentEndpointOptions
{
    public Uri? McpAddress { get; set; }

    public AgentTokenOptions Tokens { get; set; } = new();
}
