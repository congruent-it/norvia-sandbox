using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Failures;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Web.Agents;

/// <summary>
/// The app's agent API (ADR 0040): the health check as a narrow read view, which an MCP server exposes as the Estate health-check agent's
/// tools (contracts/agent-mcp/health-check.tools.json). Reads only; there is no write endpoint, and no Azure role is needed to call it.
/// </summary>
internal static class AgentEndpoints
{
    public const string Prefix = "/agent/v1";

    // The Agent narrates a run the page has just made, so it reads what that run read instead of reading the estate again (about 20 s).
    // The page's own run never reuses anything.
    private static readonly TimeSpan ReportMaxAge = TimeSpan.FromSeconds(60);

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(Prefix)
            .RequireAuthorization(AgentApiAuthentication.Policy)
            .AddEndpointFilter<AgentCallTracking>();
        group.MapGet("/checks", RunChecksAsync);
        group.MapGet("/failed-runs", ListFailedRunsAsync);
        group.MapGet("/dead-letter-queues", ListDeadLetterQueuesAsync);

        // The FinOps agent's two reads (#511), behind a role of their own: the health-check Agent's token opens neither.
        var cost = endpoints.MapGroup(Prefix)
            .RequireAuthorization(AgentApiAuthentication.CostPolicy)
            .AddEndpointFilter<AgentCallTracking>();
        cost.MapGet("/cost", GetCostAsync);
        cost.MapGet("/rising-spend", ListRisingSpendAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<AgentCostView>, ProblemHttpResult>> GetCostAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCostViewQuery(), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(AgentViewMapping.Of(result.Value))
            : TypedResults.Problem("The estate's spend could not be read.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Ok<AgentFindingsView>> ListRisingSpendAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RunHealthCheckQuery(ReuseWithin: ReportMaxAge), cancellationToken);
        return TypedResults.Ok(AgentViewMapping.Of(result.Value, RisingSpendCheck.CheckId));
    }

    private static async Task<Ok<AgentFindingsView>> RunChecksAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RunHealthCheckQuery(ReuseWithin: ReportMaxAge), cancellationToken);
        return TypedResults.Ok(AgentViewMapping.Of(result.Value));
    }

    private static async Task<Ok<AgentFailedRunsView>> ListFailedRunsAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListFailedRunsQuery(), cancellationToken);
        return TypedResults.Ok(AgentViewMapping.Of(result.Value));
    }

    private static async Task<Ok<AgentDeadLetterQueuesView>> ListDeadLetterQueuesAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListDeadLetterQueuesQuery(), cancellationToken);
        return TypedResults.Ok(AgentViewMapping.Of(result.Value));
    }
}
