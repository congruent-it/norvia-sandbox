using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Exceptions.AddNote;
using Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;
using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.Exceptions.Resolve;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.Exceptions;

public static class ExceptionEndpoints
{
    public static IEndpointRouteBuilder MapExceptions(this IEndpointRouteBuilder app)
    {
        var exceptions = app.NewVersionedApi("Exceptions")
            .MapGroup("/v{version:apiVersion}/exceptions")
            .HasApiVersion(1);

        // Like the other Planner lists, this answers any caller with a TMS token until it gets its own role.
        exceptions.MapGet("/open", ListOpenAsync)
            .RequireAuthorization()
            .WithName("ListOpenExceptions")
            .WithSummary("List the Open Exceptions, oldest first.")
            .WithDescription(
                "Each Exception shows its Exception number, its Shipment, the Shipper, who carried the Shipment (Own fleet or the Carrier), its type, "
                + "when it opened, how many causes it has, whether the Shipment was Delivered already when it opened and whether its cause is cleared. A resolved Exception leaves the list.");

        // The Planner resolves from the TMS UI, as they assign and release, on a Message trail of its own.
        exceptions.MapPost("/{exceptionNumber}/resolve", ResolveAsync)
            .WithName("ResolveException")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithMetadata(new TrackedFlow(Flows.ExceptionHandling))
            .WithSummary("Resolve an Open Exception with an outcome and a remark.")
            .WithDescription(
                "The outcome is one of NoActionNeeded, Redelivered, Replanned, ReturnedToShipper and ClaimHandledOutsideTms, and the remark is required; "
                + "otherwise the call is a 400. The TMS records both and publishes ExceptionResolved on the Shipment events topic in one transaction, "
                + "under a new correlation ID, which the answer and the event carry. The Exception leaves the list of Open Exceptions. A resolution that breaks a rule is refused, "
                + "changing and publishing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: "
                + "ExceptionNotFound (404) or ExceptionNotOpen (409).");

        // A note, like a resolution, starts in the TMS UI and gets a Message trail of its own.
        exceptions.MapPost("/{exceptionNumber}/notes", AddNoteAsync)
            .WithName("AddExceptionNote")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithMetadata(new TrackedFlow(Flows.ExceptionHandling))
            .WithSummary("Add a note to an Open Exception.")
            .WithDescription(
                "The note is free text, so a phone call or an agreement made outside the TMS is on record, and both it and the Planner's name are required, and at most 2000 and 200 characters; "
                + "otherwise the call is a 400. The TMS records the note with the Planner and the time, under a new correlation ID which the answer carries, "
                + "and publishes nothing. The Exception stays Open, and the list of Open Exceptions returns its notes. A note that breaks a rule is refused, "
                + "changing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: "
                + "ExceptionNotFound (404) or ExceptionNotOpen (409).");

        // Like a resolution, the agreement starts in the TMS UI and gets a Message trail of its own.
        exceptions.MapPost("/{exceptionNumber}/delivery-window", AgreeDeliveryWindowAsync)
            .WithName("AgreeDeliveryWindow")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithMetadata(new TrackedFlow(Flows.ExceptionHandling))
            .WithSummary("Agree a delivery window for the Shipment of an Open Exception.")
            .WithDescription(
                "After agreeing a redelivery with the Carrier outside the TMS, the Planner sets the window on a Collected or In transit Shipment. "
                + "Both its earliest and its latest time are required, and the earliest may not be after the latest; otherwise the call is a 400. "
                + "The agreed window replaces the Transport Order's for Late delivery, Stale and the fallback ETA, while the Transport Order keeps the Shipper's window. "
                + "The last one set counts, and earlier ones are kept. An Idempotency-Key is required: the same agreement sent again under it is answered as before, flagged as a repeat, without agreeing or publishing again. An open Late delivery whose ETA is inside the window is marked cause cleared. "
                + "The TMS publishes ShipmentDeliveryWindowAgreed on the Shipment events topic in one transaction, under a new correlation ID, which the answer and the event carry. "
                + "An agreement that breaks a rule is refused, changing and publishing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: "
                + "ExceptionNotFound (404), ExceptionNotOpen (409), ShipmentNotInTransport (409) or DeliveryWindowInvalid (400, for a window that ends in the past).");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<OpenExceptionSummary>>> ListOpenAsync(
        IMediator mediator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListOpenExceptionsQuery(), cancellationToken));

    private static async Task<Results<Ok<ResolveExceptionResult>, ProblemHttpResult, ValidationProblem>> ResolveAsync(
        string exceptionNumber,
        ResolveExceptionRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = Check(request, out var outcome);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The action starts in the TMS UI, not at API Management, so the TMS starts its Message trail.
        var command = new ResolveExceptionCommand(exceptionNumber, outcome, request.Remark!, CorrelationId.New().Value);
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<ExceptionRefusalCode, ResolveExceptionResult>(result, "The Exception can't be resolved.", command.CorrelationId, StatusCodeOf);
    }

    private static async Task<Results<Ok<AddExceptionNoteResult>, ProblemHttpResult, ValidationProblem>> AddNoteAsync(
        string exceptionNumber,
        AddExceptionNoteRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            errors[nameof(AddExceptionNoteRequest.Text)] = ["A note needs its text."];
        }
        else if (request.Text.Length > ExceptionNote.MaxTextLength)
        {
            errors[nameof(AddExceptionNoteRequest.Text)] = [$"A note is at most {ExceptionNote.MaxTextLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Planner))
        {
            errors[nameof(AddExceptionNoteRequest.Planner)] = ["A note needs the Planner who wrote it."];
        }
        else if (request.Planner.Length > ExceptionNote.MaxPlannerLength)
        {
            errors[nameof(AddExceptionNoteRequest.Planner)] = [$"A Planner's name is at most {ExceptionNote.MaxPlannerLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var command = new AddExceptionNoteCommand(exceptionNumber, request.Text!, request.Planner!, CorrelationId.New().Value);
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<ExceptionRefusalCode, AddExceptionNoteResult>(result, "The note can't be added.", command.CorrelationId, StatusCodeOf);
    }

    private static async Task<Results<Ok<AgreeDeliveryWindowResult>, ProblemHttpResult, ValidationProblem>> AgreeDeliveryWindowAsync(
        string exceptionNumber,
        AgreeDeliveryWindowRequest request,
        [FromHeader(Name = TransportOrderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKey.MaxLength)
        {
            errors[TransportOrderEndpoints.IdempotencyKeyHeader] =
                [$"Agreeing a delivery window needs an {TransportOrderEndpoints.IdempotencyKeyHeader} of 1 to {IdempotencyKey.MaxLength} characters, the same for each retry."];
        }

        if (request.Earliest is null)
        {
            errors[nameof(AgreeDeliveryWindowRequest.Earliest)] = ["A delivery window needs its earliest time."];
        }

        if (request.Latest is null)
        {
            errors[nameof(AgreeDeliveryWindowRequest.Latest)] = ["A delivery window needs its latest time."];
        }

        if (request.Earliest > request.Latest)
        {
            errors[nameof(AgreeDeliveryWindowRequest.Earliest)] = ["A delivery window's earliest time can't be after its latest time."];
        }

        if (errors.Count > 0 || idempotencyKey is null || request is not { Earliest: { } earliest, Latest: { } latest })
        {
            return TypedResults.ValidationProblem(errors);
        }

        var command = new AgreeDeliveryWindowCommand(exceptionNumber, earliest, latest, idempotencyKey, CorrelationId.New().Value);
        var result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<ExceptionRefusalCode, AgreeDeliveryWindowResult>(result, "The delivery window can't be agreed.", command.CorrelationId, StatusCodeOf);
    }

    private static Dictionary<string, string[]> Check(ResolveExceptionRequest request, out ExceptionOutcome outcome)
    {
        var errors = new Dictionary<string, string[]>();
        outcome = default;
        // Matched against the names, as Enum.TryParse would also take a number such as "1".
        if (string.IsNullOrWhiteSpace(request.Outcome)
            || !Enum.GetNames<ExceptionOutcome>().Contains(request.Outcome, StringComparer.Ordinal)
            || !Enum.TryParse(request.Outcome, out outcome))
        {
            errors[nameof(ResolveExceptionRequest.Outcome)] =
                [$"Resolving an Exception needs an outcome: one of {string.Join(", ", Enum.GetNames<ExceptionOutcome>())}."];
        }

        if (string.IsNullOrWhiteSpace(request.Remark))
        {
            errors[nameof(ResolveExceptionRequest.Remark)] = ["Resolving an Exception needs a remark."];
        }

        return errors;
    }

    private static int StatusCodeOf(ExceptionRefusalCode code) => code switch
    {
        ExceptionRefusalCode.ExceptionNotFound => StatusCodes.Status404NotFound,
        ExceptionRefusalCode.ExceptionNotOpen or ExceptionRefusalCode.ShipmentNotInTransport => StatusCodes.Status409Conflict,
        ExceptionRefusalCode.DeliveryWindowInvalid => StatusCodes.Status400BadRequest,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The API doesn't know this refusal's status."),
    };
}
