using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Planner;

namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The Planner agent's view of the TMS (ADR 0040): reads: where a Transport Order waiting for planning can go (alone, or with the what-if tally of a plan of the list), and an Exception's timeline for a draft note. A group of its own behind
/// a role of its own, <see cref="AgentRoles.PlannerRead"/>, which only the Planner agent's identity holds. The TMS computes the options and
/// the agent chooses (#372); there is no write endpoint, and the narrowing is in the mapping: no street address, no Consignee, no file.
/// </summary>
public static class PlannerAgentEndpoints
{
    /// <summary>The <c>flow</c> of a call on the agent view, as the MCP stand-in names the server's.</summary>
    public const string Flow = "agent-view-planner";

    public static IEndpointRouteBuilder MapPlannerAgent(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.NewVersionedApi("PlannerAgent")
            .MapGroup("/v{version:apiVersion}/agent/planner")
            .HasApiVersion(1)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(AgentApiAuthentication.Scheme).RequireAuthenticatedUser().RequireRole(AgentRoles.PlannerRead))
            .WithMetadata(new TrackedFlow(Flow))
            .AddEndpointFilter<AgentCallTracking>();

        group.MapGet("/transport-orders/{transportOrderNumber}/assignment-options", GetAssignmentOptionsAsync)
            .WithName("AgentGetAssignmentOptions")
            .WithSummary("Read where a Transport Order can go, as the Planner agent: whether Own fleet fits, and each active Carrier's Carrier cost or why it cannot be priced, with the Assignment rules each meets.");

        group.MapGet("/transport-orders/{transportOrderNumber}/assignment-options/assuming/{alreadyAssumed}", GetAssignmentOptionsAssumingAsync)
            .WithName("AgentGetAssignmentOptionsAssuming")
            .WithSummary("As the assignment options, with the what-if tally of a plan of the waiting list: alreadyAssumed is the comma-separated numbers of the Transport Orders already suggested for Own fleet, which Own fleet's load check counts too. Nothing is assigned.");

        group.MapGet("/exceptions/{exceptionNumber}/timeline", GetExceptionTimelineAsync)
            .WithName("AgentGetExceptionTimeline")
            .WithSummary("Read an Exception's timeline as the Planner agent, to draft an internal note: its type and cause, status, the Shipment's Tracking events with their sources, the Carrier booking's state, the actions already taken and the notes so far. No Consignee personal data and no file contents.");
        return app;
    }

    private static async Task<Results<Ok<PlannerAgentExceptionTimelineView>, ProblemHttpResult>> GetExceptionTimelineAsync(
        string exceptionNumber, IMediator mediator, TimeProvider time, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetExceptionTimelineQuery(exceptionNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Exception {exceptionNumber} not found.")
            : TypedResults.Ok(PlannerAgentMapping.Of(result.Value, time.GetUtcNow()));
    }

    private static Task<Results<Ok<PlannerAgentAssignmentOptionsView>, ProblemHttpResult>> GetAssignmentOptionsAsync(
        string transportOrderNumber, IMediator mediator, TimeProvider time, CancellationToken cancellationToken) =>
        ReadOptionsAsync(transportOrderNumber, null, mediator, time, cancellationToken);

    private static Task<Results<Ok<PlannerAgentAssignmentOptionsView>, ProblemHttpResult>> GetAssignmentOptionsAssumingAsync(
        string transportOrderNumber, string alreadyAssumed, IMediator mediator, TimeProvider time, CancellationToken cancellationToken) =>
        ReadOptionsAsync(transportOrderNumber, alreadyAssumed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), mediator, time, cancellationToken);

    private static async Task<Results<Ok<PlannerAgentAssignmentOptionsView>, ProblemHttpResult>> ReadOptionsAsync(
        string transportOrderNumber, IReadOnlyList<string>? alreadyAssumed, IMediator mediator, TimeProvider time, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAssignmentOptionsQuery(transportOrderNumber, alreadyAssumed), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found.")
            : TypedResults.Ok(PlannerAgentMapping.Of(result.Value, time.GetUtcNow()));
    }
}
