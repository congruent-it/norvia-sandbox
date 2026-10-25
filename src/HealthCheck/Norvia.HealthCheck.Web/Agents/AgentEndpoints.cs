using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Web.Agents;

/// <summary>
/// The app's agent API (ADR 0040): the health check as a narrow read view, which an MCP server exposes as the Estate health-check agent's
/// one tool (contracts/agent-mcp/health-check.tools.json). Reads only; there is no write endpoint, and no Azure role is needed to call it.
/// </summary>
internal static class AgentEndpoints
{
    public const string Prefix = "/agent/v1";

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(Prefix)
            .RequireAuthorization(AgentApiAuthentication.Policy)
            .AddEndpointFilter<AgentCallTracking>();
        group.MapGet("/checks", RunChecksAsync);
        return endpoints;
    }

    private static async Task<Ok<AgentFindingsView>> RunChecksAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RunHealthCheckQuery(), cancellationToken);
        return TypedResults.Ok(AgentViewMapping.Of(result.Value));
    }
}
