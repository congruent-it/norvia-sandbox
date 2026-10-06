using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
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
                + "when it opened, how many causes it has and whether the Shipment was Delivered already when it opened. A resolved Exception leaves the list.");

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

    private static Dictionary<string, string[]> Check(ResolveExceptionRequest request, out ExceptionOutcome outcome)
    {
        var errors = new Dictionary<string, string[]>();
        outcome = default;
        if (string.IsNullOrWhiteSpace(request.Outcome)
            || !Enum.TryParse(request.Outcome, ignoreCase: false, out outcome)
            || !Enum.IsDefined(outcome))
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
        _ => StatusCodes.Status409Conflict, // ExceptionNotOpen
    };
}
