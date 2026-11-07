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

    /// <summary>
    /// The Customer service agent's MCP server (<c>customer-service</c>), served at <c>/mcp/customer-service</c>. Not configured, the
    /// stand-in does not serve it.
    /// </summary>
    public HealthCheckServerOptions? CustomerService { get; set; }

    /// <summary>
    /// The Customer service reply agent's MCP server (<c>customer-service-reply</c>), served at <c>/mcp/customer-service-reply</c>. Its
    /// own audience and role, so the Customer service agent's token opens nothing here. Not configured, the stand-in does not serve it.
    /// </summary>
    public HealthCheckServerOptions? CustomerServiceReply { get; set; }

    /// <summary>
    /// The Carrier invoice matching agent's MCP server (<c>finance-matching</c>), served at <c>/mcp/finance-matching</c>. It calls the ERP
    /// API's Finance agent view. Not configured, the stand-in does not serve it.
    /// </summary>
    public HealthCheckServerOptions? FinanceMatching { get; set; }

    /// <summary>
    /// The Carrier invoice rejection reason agent's MCP server (<c>finance-rejection-reason</c>), served at <c>/mcp/finance-rejection-reason</c>.
    /// Its own audience and role, so the Carrier invoice matching agent's token opens nothing here. Not configured, the stand-in does not serve it.
    /// </summary>
    public HealthCheckServerOptions? FinanceRejectionReason { get; set; }

    /// <summary>Calls per Agent identity in a window, as the policy's <c>rate-limit-by-key</c> allows (60 a minute).</summary>
    [Range(1, 10_000)]
    public int RateLimitCalls { get; set; } = 60;

    [Range(1, 3600)]
    public int RateLimitPeriodSeconds { get; set; } = 60;
}

/// <summary>Another MCP server of the stand-in (the Estate health check's, the Customer service agent's): the tokens it accepts from its Agent, the agent API it calls, and the tokens it calls it with.</summary>
public sealed class HealthCheckServerOptions
{
    /// <summary>The Agent's tokens as the AppHost's local key signs them. The audience is the agent view's.</summary>
    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();

    /// <summary>The application's agent API: the Estate health check's, or the TMS API's Customer service agent view.</summary>
    [Required]
    public Uri? Api { get; set; }

    [Required]
    public LocalTokenOptions ApiTokens { get; set; } = new();
}
