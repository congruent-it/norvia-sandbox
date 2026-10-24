using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.IntegrationConsole.Application.DeadLetters.List;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Runs.Failed;
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

    /// <summary>How many dead-lettered messages one <c>peek_dead_letters</c> call shows.</summary>
    public const int PeekLimit = 20;

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(Prefix)
            .RequireAuthorization(AgentApiAuthentication.Policy)
            .AddEndpointFilter<AgentCallTracking>();
        group.MapGet("/trails/{correlationId}", GetTrailAsync);
        group.MapGet("/runs/{logicApp}/{workflow}/{runId}", GetRunAsync);
        group.MapGet("/failed-runs", ListFailedRunsAsync);
        group.MapGet("/dead-letter-queues", ListDeadLetterQueuesAsync);
        // A subscription is topic/subscription, whose '/' arrives as %2F (one segment) or, once a gateway has decoded it, as two segments.
        group.MapGet("/dead-letter-queues/{entity}/messages", (string entity, IMediator mediator, CancellationToken cancellationToken) =>
            PeekDeadLettersAsync(Uri.UnescapeDataString(entity), mediator, cancellationToken));
        group.MapGet("/dead-letter-queues/{topic}/{subscription}/messages", (string topic, string subscription, IMediator mediator, CancellationToken cancellationToken) =>
            PeekDeadLettersAsync($"{topic}/{subscription}", mediator, cancellationToken));
        return endpoints;
    }

    private static async Task<Ok<AgentFailedRunsView>> ListFailedRunsAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(AgentViewMapping.Of(await mediator.Send(new ListFailedRunsQuery(), cancellationToken)));

    private static async Task<Ok<AgentDeadLetterQueuesView>> ListDeadLetterQueuesAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(AgentViewMapping.Of(await mediator.Send(new ListDeadLetterQueuesQuery(), cancellationToken)));

    // A peek: the use case looks and receives nothing, so the queue is as it was.
    private static async Task<Results<Ok<AgentDeadLettersView>, NotFound, ValidationProblem>> PeekDeadLettersAsync(string entity, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListDeadLetteredMessagesQuery(entity), cancellationToken);
        return result.Status switch
        {
            Ardalis.Result.ResultStatus.Ok => TypedResults.Ok(AgentViewMapping.Of(result.Value, PeekLimit)),
            Ardalis.Result.ResultStatus.NotFound => TypedResults.NotFound(),
            _ => TypedResults.ValidationProblem(AgentViewMapping.ErrorsOf(result)),
        };
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
