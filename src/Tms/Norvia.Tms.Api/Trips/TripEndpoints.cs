using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.AddShipments;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.Discard;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Application.Trips.List;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
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

        trips.MapGet("/{number}", GetTripAsync)
            .RequireAuthorization()
            .WithName("GetTrip")
            .WithSummary("Show one Trip with its Stops in order, its load against the Vehicle's capacity, the rules it breaks now and its warnings.")
            .WithDescription(
                "Each Stop is derived from its Shipment: the collection or the delivery, its place, its window and its goods. The load counts the whole Trip as loaded at once. "
                + "failures lists the rules the Trip breaks as it stands, such as a Vehicle that has since gone Off the road; warnings lists what doesn't stop it, such as a Shipment "
                + "whose delivery window doesn't include the Trip's day. An unknown Trip is TripNotFound (404).");

        trips.MapPost("/", CreateDraftAsync)
            .WithName("CreateDraftTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Create a Draft Trip for an open day, with or without a Vehicle, Driver, planned start and planned driving duration.")
            .WithDescription(
                "Shipments to put on the Trip can be given as shipmentNumbers: they go on together or not at all, with the rules of adding Shipments to a Trip. "
                + "day and an Idempotency-Key header (the same for each retry, which is answered with the Draft the first call created) are required, otherwise the call is a 400. A Vehicle or Driver given must pass P10's availability rules and have no other Trip that day, Drafts included. "
                + "A request that breaks a rule is refused, creating nothing, with a 409 problem whose code names the first rule broken (VehicleNotAvailable, DriverNotAvailable, LicenceNotHeld, DayNotOpen, or for a Shipment ShipmentNotReleased, ShipmentOnAnotherTrip, CollectionDayMismatch, PayloadExceeded or PalletPlacesExceeded) "
                + "and whose failures lists every rule broken. A plannedStart must be inside the Driver's Shift that day (OutsideShift), and a plannedDrivingDuration (more than nothing, at most 24 hours) must fit the Driver's Driving-hour budget (DrivingHoursExceeded).");

        trips.MapPost("/{number}/shipments", AddShipmentsAsync)
            .WithName("AddShipmentsToTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Put Shipments on a Draft Trip: each adds its collection Stop and its delivery Stop at the end of the sequence.")
            .WithDescription(
                "The Shipments go on together or not at all: the Trip is checked as it would become, and a refusal leaves it as it was. The load rules count the whole Trip as loaded at once. "
                + "The problem's code names the first rule broken: ShipmentNotReleased, ShipmentOnAnotherTrip, CollectionDayMismatch, PayloadExceeded or PalletPlacesExceeded (409), "
                + "ShipmentNotFound or TripNotFound (404), or TripNotDraft (409); failures lists every rule broken. A Shipment whose delivery window doesn't include the Trip's day is accepted, "
                + "with a warning. A Shipment already on the Trip stays where it is, so sending the same call again changes nothing. Nothing is published.");

        trips.MapPut("/{number}", ChangeDraftAsync)
            .WithName("ChangeDraftTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Pick the Vehicle, Driver, planned start and planned driving duration of a Draft Trip, or give them back by leaving them out.")
            .WithDescription(
                "What is given replaces what the Draft had. A change that breaks a rule is refused, leaving the Trip as it was, with a problem whose code says which: "
                + "VehicleNotAvailable, DriverNotAvailable, LicenceNotHeld, DrivingHoursExceeded, OutsideShift or DayNotOpen (409), TripNotDraft (409) or TripNotFound (404).");

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
        [FromHeader(Name = TransportOrderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Day is null)
        {
            errors[nameof(CreateTripRequest.Day)] = ["A Trip needs its day."];
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKey.MaxLength)
        {
            errors[TransportOrderEndpoints.IdempotencyKeyHeader] =
                [$"Creating a Trip needs an {TransportOrderEndpoints.IdempotencyKeyHeader} of 1 to {IdempotencyKey.MaxLength} characters, the same for each retry."];
        }

        AddDurationError(errors, request.PlannedDrivingDuration);

        if (errors.Count > 0 || request.Day is not { } day)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(
            new CreateDraftTripCommand(
                day, VehicleOf(request.VehicleId), DriverOf(request.DriverId), new IdempotencyKey(idempotencyKey!), request.PlannedStart, request.PlannedDrivingDuration, request.ShipmentNumbers),
            cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/v1/trips/{result.Value.Number}", result.Value)
            : Refused(result.ValidationErrors, "The Trip can't be created.");
    }

    private static async Task<Results<Ok<TripDetailView>, ProblemHttpResult>> GetTripAsync(string number, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTripQuery(number), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors, "The Trip can't be shown.");
    }

    private static async Task<Results<Ok<AddShipmentsToTripResult>, ProblemHttpResult, ValidationProblem>> AddShipmentsAsync(
        string number,
        AddShipmentsRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.ShipmentNumbers is not { Count: > 0 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(AddShipmentsRequest.ShipmentNumbers)] = ["Give the numbers of the Shipments to put on the Trip."] });
        }

        var result = await mediator.Send(new AddShipmentsToTripCommand(number, request.ShipmentNumbers), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors, "The Shipments can't be put on the Trip.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> ChangeDraftAsync(
        string number,
        ChangeTripRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        AddDurationError(errors, request.PlannedDrivingDuration);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(
            new ChangeDraftTripCommand(number, VehicleOf(request.VehicleId), DriverOf(request.DriverId), request.PlannedStart, request.PlannedDrivingDuration), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors, "The Trip can't be changed.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DiscardDraftAsync(string number, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DiscardDraftTripCommand(number), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors, "The Trip can't be discarded.");
    }

    private static void AddDurationError(Dictionary<string, string[]> errors, TimeSpan? duration)
    {
        if (duration is { } value && !Trip.IsPlannableDuration(value))
        {
            errors[nameof(CreateTripRequest.PlannedDrivingDuration)] = [$"A planned driving duration is more than nothing and at most {Trip.MaxPlannedDrivingDuration.TotalHours} hours."];
        }
    }

    private static VehicleId? VehicleOf(string? id) => string.IsNullOrWhiteSpace(id) ? null : new VehicleId(id);

    private static DriverId? DriverOf(string? id) => string.IsNullOrWhiteSpace(id) ? null : new DriverId(id);

    private static ProblemHttpResult Refused(IEnumerable<ValidationError> refusals, string title)
    {
        var all = refusals.ToList();
        var first = all[0];
        return TypedResults.Problem(
            statusCode: Enum.TryParse<TripRefusalCode>(first.ErrorCode, out var code) && code is TripRefusalCode.TripNotFound or TripRefusalCode.ShipmentNotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict,
            title: title,
            detail: first.ErrorMessage,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = first.ErrorCode,
                ["failures"] = all.Select(refusal => new { code = refusal.ErrorCode, message = refusal.ErrorMessage }).ToList(),
            });
    }
}
