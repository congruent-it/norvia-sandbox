using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Trails.Open;

namespace Norvia.IntegrationConsole.Web.Agents;

/// <summary>
/// The console's agent API (ADR 0040): the existing use cases as a narrower read view, which an MCP server exposes as the
/// Integration operations agent's two tools (contracts/agent-mcp/integration-operations.tools.json). Reads only; there is no
/// write endpoint. It holds no message bodies.
/// </summary>
internal static class AgentTrailEndpoints
{
    public const string Prefix = "/agent/v1";

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(Prefix)
            .RequireAuthorization(AgentApiAuthentication.Policy)
            .AddEndpointFilter<AgentCallTracking>();
        group.MapGet("/trails/{correlationId}", GetTrailAsync);
        group.MapGet("/runs/{logicApp}/{workflow}/{runId}", GetRunAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<AgentTrailView>, ValidationProblem>> GetTrailAsync(string correlationId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new OpenTrailQuery(correlationId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AgentViewMapping.Of(result.Value)) : TypedResults.ValidationProblem(AgentViewMapping.ErrorsOf(result));
    }

    private static async Task<Results<Ok<AgentRunView>, ValidationProblem>> GetRunAsync(string logicApp, string workflow, string runId, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new OpenRunQuery(logicApp, workflow, runId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AgentViewMapping.Of(result.Value)) : TypedResults.ValidationProblem(AgentViewMapping.ErrorsOf(result));
    }
}
