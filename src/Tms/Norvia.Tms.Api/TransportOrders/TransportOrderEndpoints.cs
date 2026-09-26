using System.Security.Claims;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.GetById;
using Norvia.Tms.Application.TransportOrders.GetByNumber;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Application.TransportOrders.RatingExceptions;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

public static class TransportOrderEndpoints
{
    public const string CorrelationIdHeader = TrackingScopeMiddleware.CorrelationIdHeader;

    /// <summary>The caller's key for a re-rate, so a retry of it gets the same answer instead of a refusal.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>The app role only the intake Logic App's managed identity holds, so nothing bypasses the integration layer.</summary>
    public const string IntakeRole = "TransportOrders.Intake";

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

        // Customer service re-rates from the TMS UI, so the re-rate takes the same authorization as the queries: any
        // caller with a TMS token, until the TMS UI calls with a role of its own.
        transportOrders.MapPost("/{transportOrderNumber}/re-rate", ReRateAsync)
            .WithName("ReRateTransportOrder")
            .RequireAuthorization()
            .WithMetadata(new TrackedFlow(Flows.TransportOrderRating))
            .WithSummary("Re-rate a Transport Order whose Rating failed, typically after its Rate Card was corrected.")
            .WithDescription(
                "Rates the order again with the Rate Card version valid on its collection date, and publishes TransportOrderRated or, "
                + "when it fails again, another TransportOrderRatingFailed. An order that isn't Rating failed is refused with a 409 and "
                + "left as it is: a Rated order's Freight Charge stays frozen. A re-rate sent again under its Idempotency-Key "
                + "is answered as before, flagged as a repeat, without rating again. Like the first rating, the re-rate continues the Message "
                + "trail of the order's intake, so its event carries the intake's correlation ID.");

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
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The Transport Order can't be re-rated.",
                detail: string.Join(" ", result.Errors)),
        };
    }
}
