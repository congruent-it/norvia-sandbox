using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Exceptions.CheckLate;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.GetByNumber;
using Norvia.Tms.Application.Shipments.ListAwaitingAnswer;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Application.Shipments.ListToPutOnTrips;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Application.Shipments.Reassign;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.Shipments;

public static class ShipmentEndpoints
{
    public static IEndpointRouteBuilder MapShipments(this IEndpointRouteBuilder app)
    {
        var shipments = app.NewVersionedApi("Shipments")
            .MapGroup("/v{version:apiVersion}/shipments")
            .HasApiVersion(1);

        // Like the Transport Order reads, these answer any caller with a TMS token until they get their own role.
        shipments.MapGet("/to-release", ListToReleaseAsync)
            .RequireAuthorization()
            .WithName("ListShipmentsToRelease")
            .WithSummary("List the Shipments to release: the Planned Shipments, with who carries each, in the order they were planned.")
            .WithDescription(
                "Each Shipment that came back from a Carrier booking shows its last one's outcome and reason, when it has one. A Carrier Shipment its "
                + "Carrier's Carrier Rate Card can't price shows pricingFailure, with the code a release would be refused with (NoRateCard, "
                + "NoValidRateCardVersion, NoMatchingLane or WeightAboveHighestBreak) and a message naming the Carrier: only such a Shipment can be "
                + "released with a Spot price (P9).");

        shipments.MapGet("/to-put-on-trips", ListToPutOnTripsAsync)
            .RequireAuthorization()
            .WithName("ListShipmentsToPutOnTrips")
            .WithSummary("List the Released Own fleet Shipments not on a Trip yet, by collection time, optionally for one collection day.")
            .WithDescription(
                "Each Shipment shows its Shipper, where and when it collects and delivers, its weight and its pallets (P11). Pass day (yyyy-MM-dd) to keep the "
                + "Shipments that collect that day, the date of the collection window's earliest time in Europe/Amsterdam. A Shipment leaves the list when it is put on a Trip.");

        shipments.MapGet("/bookings-awaiting-answer", ListBookingsAwaitingAnswerAsync)
            .RequireAuthorization()
            .WithName("ListCarrierBookingsAwaitingAnswer")
            .WithSummary("List the Requested Carrier bookings awaiting an answer, sorted by earliest collection time.")
            .WithDescription(
                "Each booking shows its Shipment, Carrier, Carrier booking number, requested time, sent time or none yet, and whether it is "
                + "overdue: Requested longer than the TMS's setting, or past its Shipment's collection window's earliest time, whichever comes "
                + "first. Overdue is only a flag; nothing acts on it.");

        shipments.MapGet("/in-progress", ListInProgressAsync)
            .RequireAuthorization()
            .WithName("ListShipmentsInProgress")
            .WithSummary("List the Shipments on their way: Booked, Collected and In transit, Stale ones first, then by ETA.")
            .WithDescription(
                "Each Shipment shows its number, who carries it, its status, its last Tracking event (type, time and place), or none yet, and its "
                + "ETA: the last ETA a Carrier reported, or the delivery window's latest time when none did. A Stale Shipment, whose next expected Tracking event is late, is flagged and listed first, then the rest by ETA. "
                + "A Delivered Shipment leaves the list.");

        shipments.MapGet("/delivered-without-proof-of-delivery", ListDeliveredWithoutProofOfDeliveryAsync)
            .RequireAuthorization()
            .WithName("ListDeliveredShipmentsWithoutProofOfDelivery")
            .WithSummary("List the Delivered Shipments without a current Proof of delivery: flagged ones first, then the oldest delivered.")
            .WithDescription(
                "Each Shipment shows its number, who carries it, when it was delivered (the event time of its Delivered Tracking event), and whether its "
                + "Proof of delivery is missing: the TMS's setting (48 hours by default) has passed since then. A Shipment leaves the list, and its flag clears, when a "
                + "Proof of delivery arrives. Missing is only a flag; nothing acts on it and nothing is published.");

        shipments.MapGet("/{shipmentNumber}/tracking-events", GetTrackingTimelineAsync)
            .RequireAuthorization()
            .WithName("GetShipmentTrackingTimeline")
            .WithSummary("Read a Shipment's timeline: what the Carriers reported, and the Proofs of delivery that came in.")
            .WithDescription(
                "trackingEvents are in event-time order: each shows its type, time, place, ETA and remark, when the TMS received it, and whether it moved the "
                + "Shipment's status. proofsOfDelivery are in delivery-time order, current and superseded: each shows who received, the delivery time, "
                + "condition, remark and its files, which are read through the Proof of delivery file call, and whether it is the current one.");

        shipments.MapGet("/{shipmentNumber}", GetAsync)
            .RequireAuthorization()
            .WithName("GetShipment")
            .WithSummary(
                "Read a Shipment: its status, its Transport Order, who carries it, Own fleet or a Carrier, its history of assignments, and its Carrier bookings.");

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
                + "rule is refused, changing and publishing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: ShipmentNotFound (404), ShipmentNotPlanned "
                + "or AssignmentUnchanged (409), UnknownCarrier or InactiveCarrier (422).");

        // The Planner releases from the TMS UI, as they assign and reassign, on a Message trail of its own.
        shipments.MapPost("/{shipmentNumber}/release", ReleaseAsync)
            .WithName("ReleaseShipment")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithMetadata(new TrackedFlow(Flows.TransportPlanning))
            .WithSummary("Release a Planned Shipment, handing it over to its Carrier (P4) or to Own fleet's Trips (P11).")
            .WithDescription(
                "The Shipment becomes Released, after which it can't be reassigned or released again. For a Carrier Shipment, the TMS also "
                + "creates its Carrier booking, Requested, with a Carrier booking number. The TMS publishes ShipmentReleased on the Shipment "
                + "events topic, with the Transport Order's addresses, windows and goods lines, who carries it, and the Carrier booking number "
                + "for a Carrier Shipment, in one transaction. Each release gets a new correlation ID, which ShipmentReleased carries with the Transport Order number. A "
                + "release sent again under its Idempotency-Key is answered as before, flagged as a repeat, without releasing again. A release "
                + "that breaks an assignment rule is refused, changing and publishing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: "
                + "ShipmentNotFound (404), ShipmentNotPlanned (409), or, for a Carrier Shipment its Carrier's Carrier Rate Card can't price, NoRateCard, "
                + "NoValidRateCardVersion, NoMatchingLane or WeightAboveHighestBreak (422), naming the Carrier, and creating no Carrier booking. A Carrier Shipment "
                + "is priced at release, and its Carrier cost is frozen on the Carrier booking and carried by ShipmentReleased (P9).");

        shipments.MapPost("/{shipmentNumber}/release-with-spot-price", ReleaseWithSpotPriceAsync)
            .WithName("ReleaseShipmentWithSpotPrice")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithMetadata(new TrackedFlow(Flows.TransportPlanning))
            .WithSummary("Release a Carrier Shipment its Carrier's Carrier Rate Card can't price, at a Spot price the Planner enters (P9).")
            .WithDescription(
                "The request carries the amount in EUR without VAT, in whole cents, and a required remark. The Carrier cost frozen on the new Carrier "
                + "booking has one Freight line, the source Spot and the remark. It is otherwise a release (see ReleaseShipment), including its "
                + "Idempotency-Key. A Spot price is allowed only when the Carrier Rate Card can't price the Shipment: it is refused with "
                + "CarrierRateCardPricesShipment (409) when it can, and with SpotPriceNotAllowed (409) for Own fleet, creating and publishing nothing.");

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
        var errors = AssignmentCalls.Check(PlanningAction.Reassignment, idempotencyKey, reassignment.AssignedTo, reassignment.CarrierId);
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
            : AssignmentCalls.Refused(result, "The Shipment can't be reassigned.", command.CorrelationId);
    }

    private static async Task<Results<Ok<ReleaseShipmentResult>, ProblemHttpResult, ValidationProblem>> ReleaseAsync(
        string shipmentNumber,
        [FromHeader(Name = TransportOrderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = AssignmentCalls.Check(PlanningAction.Release, idempotencyKey);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The action starts in the TMS UI, not at API Management, so the TMS starts its Message trail.
        var command = new ReleaseShipmentCommand(shipmentNumber, idempotencyKey!, CorrelationId.New().Value);
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : AssignmentCalls.Refused(result, "The Shipment can't be released.", command.CorrelationId);
    }

    private static async Task<Results<Ok<ReleaseShipmentResult>, ProblemHttpResult, ValidationProblem>> ReleaseWithSpotPriceAsync(
        string shipmentNumber,
        [FromHeader(Name = TransportOrderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
        ReleaseWithSpotPriceRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = AssignmentCalls.Check(PlanningAction.Release, idempotencyKey);
        if (request.Amount is not > 0m || request.Amount.Value != Math.Round(request.Amount.Value, 2))
        {
            errors[nameof(ReleaseWithSpotPriceRequest.Amount)] = ["A Spot price needs an amount above zero, in whole cents."];
        }

        if (string.IsNullOrWhiteSpace(request.Remark))
        {
            errors[nameof(ReleaseWithSpotPriceRequest.Remark)] = ["A Spot price needs its remark."];
        }
        else if (request.Remark.Length > CarrierCostSource.MaxRemarkLength)
        {
            errors[nameof(ReleaseWithSpotPriceRequest.Remark)] = [$"A remark is at most {CarrierCostSource.MaxRemarkLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var command = new ReleaseShipmentCommand(
            shipmentNumber, idempotencyKey!, CorrelationId.New().Value, new SpotPrice(request.Amount!.Value, request.Remark!));
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : AssignmentCalls.Refused(result, "The Shipment can't be released with a Spot price.", command.CorrelationId);
    }

    private static async Task<Ok<IReadOnlyList<ShipmentToReleaseSummary>>> ListToReleaseAsync(
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListShipmentsToReleaseQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<CarrierBookingAwaitingAnswerSummary>>> ListBookingsAwaitingAnswerAsync(
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListCarrierBookingsAwaitingAnswerQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>>> ListDeliveredWithoutProofOfDeliveryAsync(
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<ShipmentToPutOnTripSummary>>> ListToPutOnTripsAsync(
        DateOnly? day,
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListShipmentsToPutOnTripsQuery(day), cancellationToken));

    private static async Task<Ok<IReadOnlyList<ShipmentInProgressSummary>>> ListInProgressAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // The time-based check that flags a Shipment Stale also opens a Late delivery Exception when its delivery window has
        // passed (P7). It runs first, so the list's Exception markers are up to date.
        await mediator.Send(new CheckLateDeliveriesCommand(), cancellationToken);
        return TypedResults.Ok(await mediator.Send(new ListShipmentsInProgressQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<ShipmentTimeline>, ProblemHttpResult>> GetTrackingTimelineAsync(
        string shipmentNumber,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTrackingTimelineQuery(shipmentNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Shipment {shipmentNumber} not found.")
            : TypedResults.Ok(result.Value);
    }
}
