using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Exceptions.AddNote;
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
                "The note is free text, so a phone call or an agreement made outside the TMS is on record, and both it and the Planner's name are required; "
                + "otherwise the call is a 400. The TMS records the note with the Planner and the time, under a new correlation ID which the answer carries, "
                + "and publishes nothing. The Exception stays Open, and the list of Open Exceptions returns its notes. A note that breaks a rule is refused, "
                + "changing nothing, with a problem whose code says which, and whose correlationId names the Message trail the refusal is traced on: "
                + "ExceptionNotFound (404) or ExceptionNotOpen (409).");

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

        if (string.IsNullOrWhiteSpace(request.Planner))
        {
            errors[nameof(AddExceptionNoteRequest.Planner)] = ["A note needs the Planner who wrote it."];
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
        ExceptionRefusalCode.ExceptionNotOpen => StatusCodes.Status409Conflict,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The API doesn't know this refusal's status."),
    };
}
