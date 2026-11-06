using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.TransportOrders.GetRejectedBooking;

namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The Customer service agent's view of the TMS (ADR 0040): a narrower read of the Rejected booking, which an MCP server exposes as the
/// agent's one tool (contracts/agent-mcp/customer-service.tools.json). Reads only; there is no write endpoint. Behind a token and the one
/// role <see cref="AgentRoles.CustomerServiceRead"/>, which only the agent's identity holds.
/// </summary>
public static class CustomerServiceAgentEndpoints
{
    /// <summary>The <c>flow</c> of a call on the agent view, as the MCP stand-in names the server's.</summary>
    public const string Flow = "agent-view-customer-service";

    public static IEndpointRouteBuilder MapCustomerServiceAgent(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.NewVersionedApi("CustomerServiceAgent")
            .MapGroup("/v{version:apiVersion}/agent/customer-service")
            .HasApiVersion(1)
            .RequireAuthorization(policy => policy.RequireRole(AgentRoles.CustomerServiceRead))
            .WithMetadata(new TrackedFlow(Flow))
            .AddEndpointFilter<AgentCallTracking>();

        group.MapGet("/rejected-bookings/{id:guid}", GetRejectedBookingAsync)
            .WithName("AgentGetRejectedBooking")
            .WithSummary("Read a Rejected booking as the Customer service agent: reasons and submitted content, without the Consignee's details.");
        return app;
    }

    private static async Task<Results<Ok<AgentRejectedBookingView>, ProblemHttpResult>> GetRejectedBookingAsync(Guid id, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetRejectedBookingQuery(id), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Rejected booking.")
            : TypedResults.Ok(CustomerServiceAgentMapping.Of(result.Value));
    }
}
