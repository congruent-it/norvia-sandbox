using System.Security.Claims;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Agents;
using Norvia.Tms.Api.Shipments;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.Billing.ListNotYetBillable;
using Norvia.Tms.Application.Billing.Release;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.TransportOrders.GetById;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Application.TransportOrders.RatingExceptions;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

public static class TransportOrderEndpoints
{
    public const string CorrelationIdHeader = TrackingScopeMiddleware.CorrelationIdHeader;

    /// <summary>The caller's key for a re-rate or an assignment, so a retry of it gets the same answer instead of a refusal.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>The app role only the intake Logic App's managed identity holds, so nothing bypasses the integration layer.</summary>
    public const string IntakeRole = "TransportOrders.Intake";

    /// <summary>
    /// The app role only the TMS UI's managed identity holds: Norvia's staff change Transport Orders there, such as
    /// Customer service's re-rate and the Planner's assignment.
    /// </summary>
    public const string ManageRole = "TransportOrders.Manage";

    public static IEndpointRouteBuilder MapTransportOrders(this IEndpointRouteBuilder app)
    {
        var transportOrders = app.NewVersionedApi("TransportOrders")
            .MapGroup("/v{version:apiVersion}/transport-orders")
            .HasApiVersion(1);

        transportOrders.MapPost("/", SubmitAsync)
            .WithName("SubmitTransportOrder")
            .RequireAuthorization(policy => policy.RequireRole(IntakeRole))
            .WithMetadata(new TrackedFlow(Flows.TransportOrderIntake))
            .WithSummary("Submit a Transport Order (Canonical Model) on behalf of the Shipper it names.")
            .WithDescription(
                "A booking that breaks Transport Order rules gets a 422 listing every reason. It is kept as Rejected, readable at the problem's instance. "
                + "Submitting the same booking again (same Shipper, Shipper reference and content) gets a 200 with the original Transport Order, "
                + "flagged as a repeat; reusing a Shipper reference for different content is rejected with ShipperReferenceReused.");

        // Reads get their own role with their caller, API Management's Shipper tracking (P8). Until then they answer any
        // caller with a TMS token: the API is reachable from outside Azure, and in Azure Entra issues a TMS token only
        // to a principal holding one of its roles. The TMS UI doesn't call them; it reads through the query side in process.
        var queries = transportOrders.MapGroup("").RequireAuthorization();

        queries.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetTransportOrderById")
            .WithSummary("Read a Transport Order or a Rejected booking by its ID.");

        queries.MapGet("/{transportOrderNumber}", GetAsync)
            .WithName("GetTransportOrder");

        queries.MapGet("/", ListAsync)
            .WithName("ListTransportOrders");

        queries.MapGet("/rating-exceptions", ListRatingExceptionsAsync)
            .WithName("ListRatingExceptions")
            .WithSummary("List the rating exceptions: every Transport Order whose Rating failed, with its failure code and message.");

        queries.MapGet("/not-yet-billable", ListNotYetBillableAsync)
            .WithName("ListTransportOrdersNotYetBillable")
            .WithSummary("List the Delivered Transport-only orders that aren't Billable yet, oldest delivery first, each with its blocking reasons.")
            .WithDescription(
                "Each order shows its number, Shipper, Shipper reference, when its Shipment was delivered and its blocking reasons: NotRated, "
                + "NoProofOfDelivery and OpenException. A reason disappears when its condition is met, and an order that becomes Billable leaves the list. "
                + "Orders whose Shipment isn't Delivered, Billable orders and Fulfilment orders aren't listed.");

        // Customer service re-rates from the TMS UI, which calls as its own identity with the manage role.
        transportOrders.MapPost("/{transportOrderNumber}/re-rate", ReRateAsync)
            .WithName("ReRateTransportOrder")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.TransportOrderRating))
            .WithSummary("Re-rate a Transport Order whose Rating failed, typically after its Rate Card was corrected.")
            .WithDescription(
                "Rates the order again with the Rate Card version valid on its collection date, and publishes TransportOrderRated or, "
                + "when it fails again, another TransportOrderRatingFailed. An order that isn't Rating failed is refused with a 409 and "
                + "left as it is: a Rated order's Freight Charge stays frozen. A re-rate sent again under its Idempotency-Key "
                + "is answered as before, flagged as a repeat, without rating again. Like the first rating, the re-rate continues the Message "
                + "trail of the order's intake, so its event carries the intake's correlation ID, and a refusal's problem names it as correlationId.");

        // The Planner assigns from the TMS UI, as Customer service re-rates. The action starts there, not at API Management,
        // so the TMS starts a Message trail of its own for it.
        transportOrders.MapPost("/{transportOrderNumber}/assign", AssignAsync)
            .WithName("AssignTransportOrder")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.TransportPlanning))
            .WithSummary("Assign an Accepted Transport Order to Own fleet or to a Carrier, which plans its one Shipment.")
            .WithDescription(
                "Creates the Transport Order's Shipment with a Shipment number and the status Planned, sets the Transport Order to Planned "
                + "and publishes ShipmentPlanned on the Shipment events topic, all in one transaction. The assignment gets a new correlation ID, "
                + "which ShipmentPlanned carries with the Transport Order number. An assignment sent again under its Idempotency-Key "
                + "is answered as before, flagged as a repeat, without planning again. An assignment that breaks an assignment rule is refused, "
                + "changing and publishing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: TransportOrderNotFound (404), TransportOrderNotAccepted "
                + "or TransportOrderAlreadyPlanned (409), UnknownCarrier or InactiveCarrier (422). The rating result never blocks an assignment.");

        // Customer service releases from the TMS UI, which starts the action, so the TMS starts a Message trail of its own.
        transportOrders.MapPost("/{transportOrderNumber}/release-without-proof-of-delivery", ReleaseWithoutProofOfDeliveryAsync)
            .WithName("ReleaseTransportOrderWithoutProofOfDelivery")
            .RequireAuthorization(policy => policy.RequireRole(ManageRole))
            .WithMetadata(new TrackedFlow(Flows.Billing))
            .WithSummary("Release a Delivered, Not yet billable Transport Order without a Proof of delivery.")
            .WithDescription(
                "Lets a Carrier that never sends a Proof of delivery not hold up billing for good. The remark and who releases it are required; otherwise the call is a 400. "
                + "The TMS records who released it, when and the remark, and lifts only the NoProofOfDelivery blocking reason: an order that isn't Rated or has an open Exception stays Not yet billable. "
                + "When the release completes the order, it becomes Billable and the TMS publishes one TransportOrderBillable with releasedWithoutProofOfDelivery set, in the same transaction, "
                + "under a new correlation ID, which the answer and the event carry. The release publishes nothing of its own. A release that breaks a rule is refused, "
                + "changing and publishing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: "
                + "TransportOrderNotFound (404), or AlreadyBillable, NotBilledSeparately, ShipmentNotDelivered or AlreadyReleased (409).");

        return app;
    }

    private static async Task<Results<Created<AcceptedTransportOrder>, Ok<AcceptedTransportOrder>, ProblemHttpResult, ValidationProblem>> SubmitAsync(
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        CanonicalTransportOrder transportOrder,
        ClaimsPrincipal caller,
        IntakeCallers intakeCallers,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // The channel follows from who calls, so a caller with the intake role can't submit for another channel.
        if (!intakeCallers.TryFindChannel(caller, out var intakeChannel))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The caller is not registered for an intake channel.");
        }

        var errors = CanonicalShape.Check(transportOrder, correlationId);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = (await mediator.Send(new SubmitTransportOrderCommand(transportOrder, intakeChannel, correlationId!), cancellationToken)).Value;
        if (result.Status == TransportOrderStatus.Rejected)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The Transport Order was rejected.",
                detail: "The booking breaks one or more Transport Order rules. Every reason is listed.",
                instance: $"/v1/transport-orders/{result.Id}",
                extensions: new Dictionary<string, object?> { ["reasons"] = result.RejectionReasons });
        }

        var accepted = new AcceptedTransportOrder(result.TransportOrderNumber!, result.Status, result.Repeat);
        return result.Repeat
            ? TypedResults.Ok(accepted)
            : TypedResults.Created($"/v1/transport-orders/{result.TransportOrderNumber}", accepted);
    }

    private static async Task<Results<Ok<TransportOrderView>, ProblemHttpResult>> GetAsync(
        string transportOrderNumber,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransportOrderQuery(transportOrderNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found.")
            : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<TransportOrderView>, ProblemHttpResult>> GetByIdAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTransportOrderByIdQuery(id), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {id} not found.")
            : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<IReadOnlyList<TransportOrderSummary>>> ListAsync(
        [FromQuery] TransportOrderStatus status,
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListTransportOrdersQuery(status), cancellationToken));

    private static async Task<Ok<IReadOnlyList<RatingExceptionSummary>>> ListRatingExceptionsAsync(
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListRatingExceptionsQuery(), cancellationToken));

    private static async Task<Ok<IReadOnlyList<TransportOrderNotYetBillableSummary>>> ListNotYetBillableAsync(
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListTransportOrdersNotYetBillableQuery(), cancellationToken));

    private static async Task<Results<Created<AssignTransportOrderResult>, Ok<AssignTransportOrderResult>, ProblemHttpResult, ValidationProblem>> AssignAsync(
        string transportOrderNumber,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        AssignTransportOrderRequest assignment,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = AssignmentCalls.Check(PlanningAction.Assignment, idempotencyKey, assignment.AssignedTo, assignment.CarrierId);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var carrierId = assignment.AssignedTo == AssignedTo.Carrier ? assignment.CarrierId : null;
        // The action starts in the TMS UI, not at API Management, so the TMS starts its Message trail.
        var command = new AssignTransportOrderCommand(transportOrderNumber, carrierId, idempotencyKey!, CorrelationId.New().Value);
        var result = await mediator.Send(command, cancellationToken);
        if (result.IsSuccess)
        {
            return result.Value.Repeat
                ? TypedResults.Ok(result.Value)
                : TypedResults.Created($"/v1/shipments/{result.Value.ShipmentNumber}", result.Value);
        }

        return AssignmentCalls.Refused(result, "The Transport Order can't be assigned.", command.CorrelationId);
    }

    private static async Task<Results<Ok<RateTransportOrderResult>, ProblemHttpResult, ValidationProblem>> ReRateAsync(
        string transportOrderNumber,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKey.MaxLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [IdempotencyKeyHeader] = [$"A re-rate needs an {IdempotencyKeyHeader} of 1 to {IdempotencyKey.MaxLength} characters, the same for each retry."],
            });
        }

        var result = await mediator.Send(new RateTransportOrderCommand(transportOrderNumber, ReRateKey: idempotencyKey), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Ok => TypedResults.Ok(result.Value),
            ResultStatus.NotFound => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Transport Order {transportOrderNumber} not found."),
            // A refusal names the intake's trail it is traced on, so the TMS UI can join it.
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The Transport Order can't be re-rated.",
                detail: string.Join(" ", result.Errors),
                extensions: new Dictionary<string, object?> { ["correlationId"] = result.CorrelationId }),
        };
    }

    private static async Task<Results<Ok<ReleaseWithoutProofOfDeliveryResult>, ProblemHttpResult, ValidationProblem>> ReleaseWithoutProofOfDeliveryAsync(
        string transportOrderNumber,
        ReleaseWithoutProofOfDeliveryRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Remark))
        {
            errors[nameof(ReleaseWithoutProofOfDeliveryRequest.Remark)] = ["A release without a Proof of delivery needs its remark."];
        }
        else if (request.Remark.Length > TransportOrder.MaxReleaseRemarkLength)
        {
            errors[nameof(ReleaseWithoutProofOfDeliveryRequest.Remark)] = [$"A remark is at most {TransportOrder.MaxReleaseRemarkLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(request.ReleasedBy))
        {
            errors[nameof(ReleaseWithoutProofOfDeliveryRequest.ReleasedBy)] = ["A release needs who released it."];
        }
        else if (request.ReleasedBy.Length > TransportOrder.MaxReleasedByLength)
        {
            errors[nameof(ReleaseWithoutProofOfDeliveryRequest.ReleasedBy)] = [$"A name is at most {TransportOrder.MaxReleasedByLength} characters."];
        }

        // The Agent's name and run come together or not at all, and are short: they are only for the trace.
        if (string.IsNullOrWhiteSpace(request.AgentName) != string.IsNullOrWhiteSpace(request.AgentRunId))
        {
            errors[nameof(ReleaseWithoutProofOfDeliveryRequest.AgentRunId)] = ["The Agent and its run ID come together."];
        }
        else if (request.AgentName?.Length > 100 || request.AgentRunId?.Length > 200)
        {
            errors[nameof(ReleaseWithoutProofOfDeliveryRequest.AgentRunId)] = ["The Agent's name is at most 100 characters and its run ID 200."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var actingOn = string.IsNullOrWhiteSpace(request.AgentName) ? null : new AgentRun(request.AgentName.Trim(), request.AgentRunId!.Trim());
        var command = new ReleaseWithoutProofOfDeliveryCommand(transportOrderNumber, request.Remark!, request.ReleasedBy!, CorrelationId.New().Value, actingOn);
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<BillingRefusalCode, ReleaseWithoutProofOfDeliveryResult>(result, "The Transport Order can't be released without a Proof of delivery.", command.CorrelationId, StatusCodeOf);
    }

    private static int StatusCodeOf(BillingRefusalCode code) => code switch
    {
        BillingRefusalCode.TransportOrderNotFound => StatusCodes.Status404NotFound,
        BillingRefusalCode.AlreadyBillable
            or BillingRefusalCode.NotBilledSeparately
            or BillingRefusalCode.ShipmentNotDelivered
            or BillingRefusalCode.AlreadyReleased => StatusCodes.Status409Conflict,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The API doesn't know this refusal's status."),
    };
}
