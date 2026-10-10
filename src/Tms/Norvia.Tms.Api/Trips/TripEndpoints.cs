using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.Discard;
using Norvia.Tms.Application.Trips.List;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Api.Trips;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTrips(this IEndpointRouteBuilder app)
    {
        var trips = app.NewVersionedApi("Trips")
            .MapGroup("/v{version:apiVersion}/trips")
            .HasApiVersion(1);

        // The Planner works from the TMS UI. Creating, changing and discarding a Draft publish nothing, so they start no Message trail.
        trips.MapGet("/", ListTripsAsync)
            .RequireAuthorization()
            .WithName("ListTrips")
            .WithSummary("List the Trips of today and later, by day.")
            .WithDescription("Each Trip shows its status (Draft or Dispatched), its day, and its Vehicle and Driver when it has them.");

        trips.MapPost("/", CreateDraftAsync)
            .WithName("CreateDraftTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Create a Draft Trip for an open day, with or without a Vehicle and Driver.")
            .WithDescription(
                "day is required, otherwise the call is a 400. A Vehicle or Driver given must pass P10's availability rules and have no other Trip that day, Drafts included. "
                + "A request that breaks a rule is refused, creating nothing, with a 409 problem whose code names the first rule broken (VehicleNotAvailable, DriverNotAvailable, LicenceNotHeld or DayNotOpen) "
                + "and whose failures lists every rule broken.");

        trips.MapPut("/{number}", ChangeDraftAsync)
            .WithName("ChangeDraftTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Pick the Vehicle and Driver of a Draft Trip, or give them back by leaving them out.")
            .WithDescription(
                "The Vehicle and Driver replace the Draft's. A change that breaks a rule is refused, leaving the Trip as it was, with a problem whose code says which: "
                + "VehicleNotAvailable, DriverNotAvailable, LicenceNotHeld or DayNotOpen (409), TripNotDraft (409) or TripNotFound (404).");

        trips.MapDelete("/{number}", DiscardDraftAsync)
            .WithName("DiscardDraftTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Discard a Draft Trip, which gives its Vehicle and Driver back.")
            .WithDescription("A Dispatched Trip is never deleted: it is refused with TripNotDraft (409). An unknown Trip is TripNotFound (404).");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<TripView>>> ListTripsAsync(IMediator mediator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await mediator.Send(new ListTripsQuery(), cancellationToken));

    private static async Task<Results<Created<CreateDraftTripResult>, ProblemHttpResult, ValidationProblem>> CreateDraftAsync(
        CreateTripRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.Day is not { } day)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(CreateTripRequest.Day)] = ["A Trip needs its day."] });
        }

        var result = await mediator.Send(new CreateDraftTripCommand(day, VehicleOf(request.VehicleId), DriverOf(request.DriverId)), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/v1/trips/{result.Value.Number}", result.Value)
            : Refused(result.ValidationErrors, "The Trip can't be created.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangeDraftAsync(
        string number,
        ChangeTripRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ChangeDraftTripCommand(number, VehicleOf(request.VehicleId), DriverOf(request.DriverId)), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors, "The Trip can't be changed.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DiscardDraftAsync(string number, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DiscardDraftTripCommand(number), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors, "The Trip can't be discarded.");
    }

    private static VehicleId? VehicleOf(string? id) => string.IsNullOrWhiteSpace(id) ? null : new VehicleId(id);

    private static DriverId? DriverOf(string? id) => string.IsNullOrWhiteSpace(id) ? null : new DriverId(id);

    private static ProblemHttpResult Refused(IEnumerable<ValidationError> refusals, string title)
    {
        var all = refusals.ToList();
        var first = all[0];
        return TypedResults.Problem(
            statusCode: first.ErrorCode == nameof(TripRefusalCode.TripNotFound) ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict,
            title: title,
            detail: first.ErrorMessage,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = first.ErrorCode,
                ["failures"] = all.Select(refusal => new { code = refusal.ErrorCode, message = refusal.ErrorMessage }).ToList(),
            });
    }
}
