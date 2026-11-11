using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Reply;
using Norvia.Tms.Application.Exceptions.ShipperNotificationTemplate;

namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The Planner notification agent's view of the TMS (ADR 0011, ADR 0040): two reads, the template of a Notify Shipper message for an
/// Exception and what the Shipper sees of a Transport Order. A group of its own behind a role of its own,
/// <see cref="AgentRoles.PlannerNotificationRead"/>, so the identity cannot call any other Agent's view and no other identity calls this
/// one. The boundary is the tool: the Agent cannot name the Carrier or an internal remark or Exception because nothing it can call returns
/// one. There is no write endpoint: the Planner sends the notification through Notify Shipper (P7).
/// </summary>
public static class PlannerNotificationAgentEndpoints
{
    /// <summary>The <c>flow</c> of a call on the agent view, as the MCP stand-in names the server's.</summary>
    public const string Flow = "agent-view-planner-notification";

    public static IEndpointRouteBuilder MapPlannerNotificationAgent(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.NewVersionedApi("PlannerNotificationAgent")
            .MapGroup("/v{version:apiVersion}/agent/planner-notification")
            .HasApiVersion(1)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(AgentApiAuthentication.Scheme).RequireAuthenticatedUser().RequireRole(AgentRoles.PlannerNotificationRead))
            .WithMetadata(new TrackedFlow(Flow))
            .AddEndpointFilter<AgentCallTracking>();

        group.MapGet("/exceptions/{exceptionNumber}/shipper-notification-template", GetTemplateAsync)
            .WithName("AgentGetShipperNotificationTemplate")
            .WithSummary("Read the message Notify Shipper starts with for an Exception's type, as the Planner notification agent. Only the template's words: no Carrier, no remark and no Exception details.");

        group.MapGet("/transport-orders/{transportOrderNumber}/shipper-view", GetShipperViewAsync)
            .WithName("AgentGetShipperViewForNotification")
            .WithSummary("Read what the Shipper sees of a Transport Order, as the Planner notification agent: Tracking status, progress, Proof of delivery availability and the Shipper notifications already sent.");
        return app;
    }

    internal static async Task<Results<Ok<PlannerNotificationTemplateView>, ProblemHttpResult>> GetTemplateAsync(
        string exceptionNumber, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ShipperNotificationTemplateQuery(exceptionNumber), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(new PlannerNotificationTemplateView(result.Value.Message))
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Exception {exceptionNumber} not found.");
    }

    internal static async Task<Results<Ok<ReplyAgentTrackingView>, ProblemHttpResult>> GetShipperViewAsync(
        string transportOrderNumber, IMediator mediator, TimeProvider time, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetShipperViewQuery(transportOrderNumber), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(CustomerServiceReplyAgentMapping.Of(result.Value, time.GetUtcNow()))
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found.");
    }
}

/// <summary>
/// The template of a Notify Shipper message as the notification agent reads it (<c>get_shipper_notification_template</c>): the template's
/// words, which the Shipper receives anyway. No Exception number, type or cause, no Carrier and no remark.
/// </summary>
/// <param name="Message">The template's words for the Exception's type, with the Shipper's reference filled in.</param>
public sealed record PlannerNotificationTemplateView(string Message);
