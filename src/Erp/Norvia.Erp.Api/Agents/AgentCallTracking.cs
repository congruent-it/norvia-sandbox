using System.Security.Claims;
using Microsoft.Extensions.Options;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Api.Agents;

/// <summary>
/// Puts a call on an agent view on the Message trail as a hop of the ERP (coding guidelines §7): the correlation ID the MCP server
/// carries (the tracking middleware has set it), the Agent's identity and the tool name the server sets. The identity comes from the
/// validated token (never from a header, ADR 0005), except behind API Management in Azure, where the token is the gateway's and the
/// gateway's policy sets the header, which only the gateway's own token may be believed on.
/// </summary>
internal sealed partial class AgentCallTracking(IOptions<AgentApiOptions> options, ILogger<AgentCallTracking> logger) : IEndpointFilter
{
    public const string ToolHeader = "X-Tool-Name";
    public const string AgentHeader = "X-Agent-ID";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var keys = new Dictionary<string, object?>();
        if (AgentIdOf(http) is { Length: > 0 } agentId)
        {
            keys[TrackingKeys.AgentId] = agentId;
        }

        if (http.Request.Headers[ToolHeader].ToString() is { Length: > 0 and <= 100 } toolName)
        {
            keys[TrackingKeys.ToolName] = toolName;
        }

        using var trail = logger.BeginScope(keys);
        var result = await next(context);
        using var outcome = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.Outcome] = OutcomeOf(result) });
        LogRead(logger);
        return result;
    }

    private string? AgentIdOf(HttpContext http)
    {
        var caller = http.User.FindFirstValue("azp");
        var gateway = options.Value.GatewayClientId;
        return gateway.Length > 0 && string.Equals(caller, gateway, StringComparison.OrdinalIgnoreCase)
            ? http.Request.Headers[AgentHeader].ToString()
            : caller;
    }

    // A problem is the agent view refusing the call; anything else that got this far was read.
    private static string OutcomeOf(object? result) =>
        (result is INestedHttpResult nested ? nested.Result : result) is IStatusCodeHttpResult { StatusCode: >= 400 } ? TrackingKeys.Refused : TrackingKeys.Succeeded;

    [LoggerMessage(Level = LogLevel.Information, Message = "The agent view was read")]
    private static partial void LogRead(ILogger logger);
}
