using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Reply;

namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The Customer service reply agent's view of the TMS (ADR 0011, ADR 0040): one read, what the Shipper sees of a Transport Order. It is a
/// group of its own behind a role of its own, <see cref="AgentRoles.CustomerServiceReplyRead"/>, so the reply agent's identity cannot call
/// the Customer service agent's view and the other way round. The boundary is the tool: the Agent cannot name a Carrier or an
/// internal Exception because nothing it can call returns one.
/// </summary>
public static class CustomerServiceReplyAgentEndpoints
{
    /// <summary>The <c>flow</c> of a call on the agent view, as the MCP stand-in names the server's.</summary>
    public const string Flow = "agent-view-customer-service-reply";

    public static IEndpointRouteBuilder MapCustomerServiceReplyAgent(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.NewVersionedApi("CustomerServiceReplyAgent")
            .MapGroup("/v{version:apiVersion}/agent/customer-service-reply")
            .HasApiVersion(1)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(AgentApiAuthentication.Scheme).RequireAuthenticatedUser().RequireRole(AgentRoles.CustomerServiceReplyRead))
            .WithMetadata(new TrackedFlow(Flow))
            .AddEndpointFilter<AgentCallTracking>();

        group.MapGet("/transport-orders/{transportOrderNumber}/shipper-view", GetShipperViewAsync)
            .WithName("AgentGetShipperView")
            .WithSummary("Read what the Shipper sees of a Transport Order, as the Customer service reply agent: Tracking status, progress, Proof of delivery availability and the Shipper notifications already sent.");
        return app;
    }

    private static async Task<Results<Ok<ReplyAgentTrackingView>, ProblemHttpResult>> GetShipperViewAsync(
        string transportOrderNumber, IMediator mediator, TimeProvider time, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetShipperViewQuery(transportOrderNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found.")
            : TypedResults.Ok(CustomerServiceReplyAgentMapping.Of(result.Value, time.GetUtcNow()));
    }
}
