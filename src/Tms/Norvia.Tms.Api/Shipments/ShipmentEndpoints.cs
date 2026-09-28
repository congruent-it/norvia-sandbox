using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.GetByNumber;
using Norvia.Tms.Application.Shipments.Reassign;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.Shipments;

public static class ShipmentEndpoints
{
    public static IEndpointRouteBuilder MapShipments(this IEndpointRouteBuilder app)
    {
        var shipments = app.NewVersionedApi("Shipments")
            .MapGroup("/v{version:apiVersion}/shipments")
            .HasApiVersion(1);

        // Like the Transport Order reads, this answers any caller with a TMS token until it gets its own role.
        shipments.MapGet("/{shipmentNumber}", GetAsync)
            .RequireAuthorization()
            .WithName("GetShipment")
            .WithSummary("Read a Shipment: its status, its Transport Order, who carries it, Own fleet or a Carrier, and its history of assignments.");

        // The Planner reassigns from the TMS UI, as they assign. The action starts there, not at API Management, so the
        // TMS starts a Message trail of its own for it.
        shipments.MapPost("/{shipmentNumber}/reassign", ReassignAsync)
            .WithName("ReassignShipment")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithMetadata(new TrackedFlow(Flows.TransportPlanning))
            .WithSummary("Reassign a Planned Shipment to Own fleet or another Carrier, to fix a mistake before release.")
            .WithDescription(
                "The Shipment and its Shipment number stay the same. The TMS records the reassignment in the Shipment's history of assignments "
                + "and publishes ShipmentReassigned on the Shipment events topic, with who carried it before, in one transaction. Each reassignment "
                + "gets a new correlation ID, which ShipmentReassigned carries with the Transport Order number. A reassignment sent again under its "
                + "Idempotency-Key is answered as before, flagged as a repeat, without reassigning again. A reassignment that breaks an assignment "
                + "rule is refused, changing and publishing nothing, with a problem whose code says which: ShipmentNotFound (404), ShipmentNotPlanned "
                + "or AssignmentUnchanged (409), UnknownCarrier or InactiveCarrier (422).");

        return app;
    }

    private static async Task<Results<Ok<ShipmentView>, ProblemHttpResult>> GetAsync(
        string shipmentNumber,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetShipmentQuery(shipmentNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Shipment {shipmentNumber} not found.")
            : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<ReassignShipmentResult>, ProblemHttpResult, ValidationProblem>> ReassignAsync(
        string shipmentNumber,
        [FromHeader(Name = TransportOrderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
        ReassignShipmentRequest reassignment,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = AssignmentCalls.Check("A reassignment", idempotencyKey, reassignment.AssignedTo, reassignment.CarrierId);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var carrierId = reassignment.AssignedTo == AssignedTo.Carrier ? reassignment.CarrierId : null;
        // The action starts in the TMS UI, not at API Management, so the TMS starts its Message trail.
        var command = new ReassignShipmentCommand(shipmentNumber, carrierId, idempotencyKey!, CorrelationId.New().Value);
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : AssignmentCalls.Refused(result, "The Shipment can't be reassigned.");
    }
}
