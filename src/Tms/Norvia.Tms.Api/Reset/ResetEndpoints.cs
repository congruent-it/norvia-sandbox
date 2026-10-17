using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Application.Reset;

namespace Norvia.Tms.Api.Reset;

public static class ResetEndpoints
{
    /// <summary>The app role only the Simulator's identity holds, for returning the estate to a clean sheet.</summary>
    public const string ResetRole = "Estate.Reset";

    public static IEndpointRouteBuilder MapReset(this IEndpointRouteBuilder app)
    {
        var reset = app.NewVersionedApi("Reset")
            .MapGroup("/v{version:apiVersion}/reset")
            .HasApiVersion(1)
            .RequireAuthorization(policy => policy.RequireRole(ResetRole));

        // Neither call publishes anything or starts a Message trail: the reset is the presenter's, not a flow of the estate.
        reset.MapGet("/", GetReadinessAsync)
            .WithName("GetResetReadiness")
            .WithSummary("Whether a reset would be taken now.")
            .WithDescription("enabled is the Reset setting, which is off unless the estate switches it on. busySubscription names the first topic subscription that holds unprocessed messages, as topic/subscription, or is null when all are empty.");

        reset.MapPost("/", ResetAsync)
            .WithName("ResetEstate")
            .WithSummary("Delete the TMS's transactional records and put the seed file's Rosters back in force.")
            .WithDescription(
                "Deletes the Transport Orders and everything hanging off them (Shipments, Trips, Exceptions, Carrier bookings, Tracking events, Proof of delivery records and files, Shipper notifications and outbox Messages) and the Planner's changes to Rosters, Absences and Off the road days (the open Planning days they shaped are opened again from the seed file's Rosters), and restarts the number sequences. "
                + "Rate Cards, Carriers, Vehicles, Drivers and Shippers are reference data and stay. Refused, deleting nothing, with a 403 problem (code ResetDisabled) while the setting is off, and a 409 problem (code SubscriptionBusy) naming the subscription while one holds unprocessed messages.");

        return app;
    }

    private static async Task<Ok<ResetReadiness>> GetReadinessAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new GetResetReadinessQuery(), cancellationToken));

    private static async Task<Results<Ok<ResetReport>, ProblemHttpResult>> ResetAsync(IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ResetEstateCommand(), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Ok => TypedResults.Ok(result.Value),
            ResultStatus.Forbidden => TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "The reset is switched off.", detail: result.Errors.First(),
                extensions: new Dictionary<string, object?> { ["code"] = ResetEstateHandler.DisabledCode }),
            _ => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "The estate is not quiet.", detail: result.Errors.First(),
                extensions: new Dictionary<string, object?> { ["code"] = ResetEstateHandler.SubscriptionBusyCode }),
        };
    }
}
