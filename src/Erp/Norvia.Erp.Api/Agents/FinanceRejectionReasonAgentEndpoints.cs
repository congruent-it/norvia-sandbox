using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application.Agents;
using Norvia.Erp.Application.Agents.RejectionReason;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Api.Agents;

/// <summary>
/// The Carrier invoice rejection reason agent's view of the ERP (ADR 0040, #473): one read, what the Carrier can see of its own line. It is a
/// group of its own behind a role of its own, <see cref="AgentRoles.FinanceRejectionReasonRead"/>, so that Agent's identity cannot call the
/// Finance agent view and the other way round. The boundary is the tool: the reason leaves Norvia, and the Agent cannot name the Planner's
/// remark, a Rate Card version or another Carrier's reference because nothing it can call returns one.
/// </summary>
public static class FinanceRejectionReasonAgentEndpoints
{
    /// <summary>The <c>flow</c> of a call on the agent view, as the MCP stand-in names the server's.</summary>
    public const string Flow = "agent-view-finance-rejection-reason";

    public static IEndpointRouteBuilder MapFinanceRejectionReasonAgent(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.NewVersionedApi("FinanceRejectionReasonAgent")
            .MapGroup("/v{version:apiVersion}/agent/finance-rejection-reason")
            .HasApiVersion(1)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(AgentApiAuthentication.Scheme).RequireAuthenticatedUser().RequireRole(AgentRoles.FinanceRejectionReasonRead))
            .WithMetadata(new TrackedFlow(Flow))
            .AddEndpointFilter<AgentCallTracking>();

        group.MapGet("/carriers/{carrierId}/invoices/{invoiceNumber}/lines/{carrierBookingReference}/carrier-visible-facts", GetCarrierVisibleLineFactsAsync)
            .WithName("AgentGetCarrierVisibleLineFacts")
            .WithSummary("Read what the Carrier can see of its own Carrier invoice line, as the rejection reason agent: the amounts, the likely correct reference of its own booking and its own earlier invoice of a Duplicate.");
        return app;
    }

    internal static async Task<Results<Ok<CarrierVisibleLineFactsView>, ProblemHttpResult>> GetCarrierVisibleLineFactsAsync(
        string carrierId, string invoiceNumber, string carrierBookingReference, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCarrierVisibleLineFactsQuery(new CarrierId(carrierId), invoiceNumber, carrierBookingReference), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Carrier invoice line.");
    }
}
