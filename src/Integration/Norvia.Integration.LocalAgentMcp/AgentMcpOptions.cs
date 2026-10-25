using System.ComponentModel.DataAnnotations;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalAgentMcp;

/// <summary>The stand-in's settings: the tokens it accepts from Agents, the application's agent view it calls, and the rate limit.</summary>
public sealed class AgentMcpOptions
{
    public const string SectionName = "AgentMcp";

    /// <summary>The Agents' tokens as the AppHost's local key signs them. The audience is the agent view's.</summary>
    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();

    /// <summary>The Integration console's agent API: where the tools are read, and the tokens the stand-in calls it with as the Agent's identity.</summary>
    [Required]
    public Uri? ConsoleAgentApi { get; set; }

    [Required]
    public LocalTokenOptions ConsoleTokens { get; set; } = new();

    /// <summary>
    /// The Estate health check's MCP server (<c>health-check</c>), served at <c>/mcp/health-check</c> beside the Integration operations
    /// agent's <c>/mcp</c>. Not configured, the stand-in does not serve it.
    /// </summary>
    public HealthCheckServerOptions? HealthCheck { get; set; }

    /// <summary>Calls per Agent identity in a window, as the policy's <c>rate-limit-by-key</c> allows (60 a minute).</summary>
    [Range(1, 10_000)]
    public int RateLimitCalls { get; set; } = 60;

    [Range(1, 3600)]
    public int RateLimitPeriodSeconds { get; set; } = 60;
}

/// <summary>A second MCP server of the stand-in: the tokens it accepts from its Agent, the agent API it calls, and the tokens it calls it with.</summary>
public sealed class HealthCheckServerOptions
{
    /// <summary>The Agent's tokens as the AppHost's local key signs them. The audience is the agent view's.</summary>
    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();

    /// <summary>The Estate health check's agent API.</summary>
    [Required]
    public Uri? Api { get; set; }

    [Required]
    public LocalTokenOptions ApiTokens { get; set; } = new();
}
