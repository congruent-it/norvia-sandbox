using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Billing.GetReleaseEligibility;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments.GetByNumber;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Application.TransportOrders.GetDetail;
using Norvia.Tms.Application.TransportOrders.GetRejectedBooking;

namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The Customer service agent's view of the TMS (ADR 0040): a narrower read of a Rejected booking and of a Transport Order with its Shipment, which an MCP server exposes as the
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
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(AgentApiAuthentication.Scheme).RequireAuthenticatedUser().RequireRole(AgentRoles.CustomerServiceRead))
            .WithMetadata(new TrackedFlow(Flow))
            .AddEndpointFilter<AgentCallTracking>();

        group.MapGet("/rejected-bookings/{id:guid}", GetRejectedBookingAsync)
            .WithName("AgentGetRejectedBooking")
            .WithSummary("Read a Rejected booking as the Customer service agent: reasons and submitted content, without the Consignee's details.");

        group.MapGet("/transport-orders/{transportOrderNumber}", GetTransportOrderAsync)
            .WithName("AgentGetTransportOrder")
            .WithSummary("Read a Transport Order as the Customer service agent: status, rating result, Shipment status and billing state with its blocking reasons.");
        group.MapGet("/shipments/{shipmentNumber}/tracking-events", GetShipmentTimelineAsync)
            .WithName("AgentGetShipmentTimeline")
            .WithSummary("Read a Shipment's Tracking events as the Customer service agent, with their sources and without the remarks reporters typed.");
        group.MapGet("/shipments/{shipmentNumber}/exceptions", GetExceptionsAsync)
            .WithName("AgentGetExceptions")
            .WithSummary("Read a Shipment's Exceptions as the Customer service agent: type, causes, status, the Planner's notes and whether the Shipper was notified.");
        group.MapGet("/shipments/{shipmentNumber}/proof-of-delivery-status", GetProofOfDeliveryStatusAsync)
            .WithName("AgentGetProofOfDeliveryStatus")
            .WithSummary("Read where a Shipment stands for its Proof of delivery: current, superseded, missing, reservations. Never who signed and never a file.");
        group.MapGet("/transport-orders/{transportOrderNumber}/release-eligibility", GetReleaseEligibilityAsync)
            .WithName("AgentGetReleaseEligibility")
            .WithSummary("Read whether a Transport Order may be released without a Proof of delivery, as the Customer service agent: the TMS's answer and, if not, every reason that blocks it.");
        return app;
    }

    private static async Task<Results<Ok<AgentReleaseEligibilityView>, ProblemHttpResult>> GetReleaseEligibilityAsync(
        string transportOrderNumber, IMediator mediator, TimeProvider time, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetReleaseEligibilityQuery(transportOrderNumber), cancellationToken);
        if (result.Status == ResultStatus.NotFound)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found.");
        }

        var eligibility = result.Value;
        return TypedResults.Ok(new AgentReleaseEligibilityView(
            eligibility.TransportOrderNumber, eligibility.Eligible, eligibility.ShipmentNumber, eligibility.DeliveredAt, time.GetUtcNow(), eligibility.BlockingReasons));
    }

    private static async Task<Results<Ok<AgentTransportOrderView>, ProblemHttpResult>> GetTransportOrderAsync(string transportOrderNumber, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransportOrderDetailQuery(transportOrderNumber), cancellationToken);
        if (result.Status == ResultStatus.NotFound)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found.");
        }

        var detail = result.Value;
        ShipmentTimeline? timeline = null;
        if (detail.Shipment is { } shipment)
        {
            var timelineResult = await mediator.Send(new GetTrackingTimelineQuery(shipment.ShipmentNumber), cancellationToken);
            timeline = timelineResult.IsSuccess ? timelineResult.Value : null;
        }

        return TypedResults.Ok(CustomerServiceAgentMapping.Of(detail.TransportOrder, detail.Shipment?.Status, timeline));
    }

    private static async Task<Results<Ok<AgentShipmentTimelineView>, ProblemHttpResult>> GetShipmentTimelineAsync(string shipmentNumber, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTrackingTimelineQuery(shipmentNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? NoSuchShipment(shipmentNumber)
            : TypedResults.Ok(CustomerServiceAgentMapping.Of(shipmentNumber, result.Value));
    }

    private static async Task<Results<Ok<AgentExceptionsView>, ProblemHttpResult>> GetExceptionsAsync(string shipmentNumber, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTrackingTimelineQuery(shipmentNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? NoSuchShipment(shipmentNumber)
            : TypedResults.Ok(CustomerServiceAgentMapping.ExceptionsOf(shipmentNumber, result.Value));
    }

    private static async Task<Results<Ok<AgentProofOfDeliveryStatusView>, ProblemHttpResult>> GetProofOfDeliveryStatusAsync(string shipmentNumber, IMediator mediator, CancellationToken cancellationToken)
    {
        var shipment = await mediator.Send(new GetShipmentQuery(shipmentNumber), cancellationToken);
        var timeline = shipment.IsSuccess ? await mediator.Send(new GetTrackingTimelineQuery(shipmentNumber), cancellationToken) : null;
        if (shipment.Status == ResultStatus.NotFound || timeline is null || timeline.Status == ResultStatus.NotFound)
        {
            return NoSuchShipment(shipmentNumber);
        }

        // Only a Delivered Shipment can have a Proof of delivery missing, and the TMS's setting says when.
        bool? missing = null;
        if (shipment.Value.Status == Domain.Shipments.ShipmentStatus.Delivered && !timeline.Value.ProofsOfDelivery.Any(proof => proof.IsCurrent))
        {
            var withoutProof = await mediator.Send(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), cancellationToken);
            missing = withoutProof.FirstOrDefault(row => row.ShipmentNumber == shipment.Value.ShipmentNumber)?.IsProofOfDeliveryMissing;
        }

        return TypedResults.Ok(CustomerServiceAgentMapping.ProofOfDeliveryOf(shipment.Value.ShipmentNumber, shipment.Value.Status, timeline.Value, missing));
    }

    private static ProblemHttpResult NoSuchShipment(string shipmentNumber) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Shipment {shipmentNumber} not found.");

    private static async Task<Results<Ok<AgentRejectedBookingView>, ProblemHttpResult>> GetRejectedBookingAsync(Guid id, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetRejectedBookingQuery(id), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Rejected booking.")
            : TypedResults.Ok(CustomerServiceAgentMapping.Of(result.Value));
    }
}
