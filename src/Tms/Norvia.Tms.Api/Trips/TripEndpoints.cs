using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.AddShipments;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Application.Trips.Complete;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.Discard;
using Norvia.Tms.Application.Trips.Dispatch;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Application.Trips.List;
using Norvia.Tms.Application.Trips.ReorderStops;
using Norvia.Tms.Application.Trips.ReportDelivery;
using Norvia.Tms.Application.Trips.SendAgain;
using Norvia.Tms.Application.Trips.Start;
using Norvia.Tms.Application.Trips.TakeShipmentOff;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Api.Trips;

public static class TripEndpoints
{
    /// <summary>The app role only the P11 Trip dispatch Logic App's managed identity holds, for its delivered and not-delivered reports.</summary>
    public const string ReportDeliveryRole = "Trips.ReportDelivery";

    /// <summary>The app role only the P12 Driver reports Logic App's managed identity holds, for starting a Trip.</summary>
    public const string StartRole = "Trips.Start";

    /// <summary>The app role only the P12 Driver reports Logic App's managed identity holds, for ending a Trip.</summary>
    public const string EndRole = "Trips.End";

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
            .WithSummary("Put Shipments on a Draft or Dispatched Trip: each adds its collection Stop and its delivery Stop at the end of the sequence.")
            .WithDescription(
                "The Shipments go on together or not at all: the Trip is checked as it would become, and a refusal leaves it as it was. The load rules count the whole Trip as loaded at once. "
                + "The problem's code names the first rule broken: ShipmentNotReleased, ShipmentOnAnotherTrip, CollectionDayMismatch, PayloadExceeded or PalletPlacesExceeded (409), "
                + "ShipmentNotFound or TripNotFound (404), or TripUnderWay (409) for a Trip that is In progress; failures lists every rule broken. A Shipment whose delivery window doesn't include the Trip's day is accepted, "
                + "with a warning. A Shipment already on the Trip stays where it is, so sending the same call again changes nothing. A Draft publishes nothing. On a Dispatched Trip this is an Amend: in the same transaction the version goes up, "
                + "the added Shipments become Dispatched, and exactly one TripDispatched with the whole Trip is written to the outbox under a new correlation ID, which the X-Correlation-ID response header returns.");

        trips.MapPost("/{number}/shipments/{shipmentNumber}/take-off", TakeShipmentOffAsync)
            .WithName("TakeShipmentOffTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Take a Shipment off its Trip until it is Collected, with a reason; it goes back to Planned.")
            .WithDescription(
                "In one transaction the Shipment's two Stops leave the Trip, the Shipment goes back to Planned (its Tracking status stays Scheduled) and exactly one ShipmentTakenOffTrip is written to the outbox for the Shipment events topic under a new correlation ID, "
                + "returned in the answer and the X-Correlation-ID header. On a Dispatched Trip, also one that is In progress, this is an Amend too: the version goes up and a TripDispatched with the Shipments that remain, possibly none, is written under the same correlation ID. "
                + "reason is required. Not idempotent: sending it again after it succeeded is refused with ShipmentNotOnTrip, since the Shipment has left. Refused with ShipmentCollected (409) once the Shipment is Collected, ShipmentNotOnTrip (409), VehicleOrDriverInactive (409) for a sent Trip whose Vehicle or Driver has gone inactive, TripNotFound or ShipmentNotFound (404).");

        trips.MapPut("/{number}/stops", ReorderStopsAsync)
            .WithName("ReorderTripStops")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Put the Stops of a Draft or Dispatched Trip in order.")
            .WithDescription(
                "stops names every Stop of the Trip once, as a Shipment number and Collection or Delivery, in the order the Driver visits them. Two Stops at the same address stay two Stops. "
                + "A Shipment's delivery before its collection is refused with CollectionBeforeDelivery (409), and an order that leaves out a Stop, repeats one or names another is StopsNotTheTripsStops (409); "
                + "a refusal leaves the order as it was. Also TripUnderWay (409) for a Trip that is In progress, VehicleOrDriverInactive (409) for a Dispatched Trip whose Vehicle or Driver is no longer active, or TripNotFound (404). Sending the same order again changes nothing. "
                + "A Draft publishes nothing. A new order on a Dispatched Trip is an Amend, which raises the version and writes one TripDispatched under a new correlation ID, returned in the X-Correlation-ID response header.");

        trips.MapPost("/{number}/dispatch", DispatchAsync)
            .WithName("DispatchTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Dispatch a Draft Trip to its Driver.")
            .WithDescription(
                "The Trip needs a Vehicle, a Driver, a planned start, a planned driving duration and at least one Shipment: each part it lacks is a TripIncomplete (409) and the Trip stays a Draft. "
                + "Every Trip rule is checked again, so a Draft that became invalid, such as after an Absence was recorded on its Driver, is refused with that rule's code (DriverNotAvailable, VehicleNotAvailable, LicenceNotHeld, DrivingHoursExceeded, "
                + "PayloadExceeded, PalletPlacesExceeded, DayNotOpen, OutsideShift, or for a Shipment ShipmentNotReleased, ShipmentOnAnotherTrip or CollectionDayMismatch), and nothing changes. "
                + "On success, in one transaction, the Trip becomes Dispatched at version 1, its Shipments become Dispatched, and exactly one TripDispatched (version 1, the whole Trip with its Stops in order) is written to the outbox for the Trip events topic, "
                + "under a new correlation ID that the answer returns. There is no event per Shipment. A Trip that is Dispatched already is TripNotDraft (409), not a repeat: the caller is the TMS UI, which doesn't retry a Dispatch, and a later version goes out by Amend or by sending the version again; an unknown Trip is TripNotFound (404).");

        trips.MapPost("/{number}/versions/{tripVersion:int}/send-again", SendAgainAsync)
            .WithName("SendTripVersionAgain")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Send a Not delivered version of a Trip to the Driver app again.")
            .WithDescription(
                "Only the Trip's latest version can be sent again, and only while it is Not delivered. The version is Sending once more, and, in one transaction, exactly one TripDispatched with the same version and the whole Trip as it stands now "
                + "is written to the outbox for the Trip events topic, under a new correlation ID that the answer returns. NothingToSendAgain (409) for a version that is Sending or Delivered, TripVersionNotLatest (409) for an older version, "
                + "TripNotDispatched (409) for a Draft, VehicleOrDriverInactive (409) when the Trip's Vehicle or Driver is no longer active, TripVersionNotFound or TripNotFound (404).");

        trips.MapPost("/{number}/versions/{tripVersion:int}/report-delivered", ReportDeliveredAsync)
            .WithName("ReportTripVersionDelivered")
            .RequireAuthorization(policy => policy.RequireRole(ReportDeliveryRole))
            .WithMetadata(new TrackedFlow(Flows.TripDispatch))
            .WithSummary("Report that the Driver app accepted a version of a Trip.")
            .WithDescription(
                "Sets the version's delivery state to Delivered to Driver app and publishes nothing. A report for a version that is no longer Sending (a repeat, or one that arrives after the other report) is ignored and logged: it changes and publishes nothing. "
                + "A report for a version older than the Trip's latest is stored against that version and changes nothing else. "
                + "The X-Correlation-ID is the correlation ID of the Dispatch or send-again, which the TripDispatched event carried. TripNotFound or TripVersionNotFound (404, with that code).");

        trips.MapPost("/{number}/versions/{tripVersion:int}/report-not-delivered", ReportNotDeliveredAsync)
            .WithName("ReportTripVersionNotDelivered")
            .RequireAuthorization(policy => policy.RequireRole(ReportDeliveryRole))
            .WithMetadata(new TrackedFlow(Flows.TripDispatch))
            .WithSummary("Report that a version of a Trip never reached the Driver app, with its technical cause.")
            .WithDescription(
                "Sets the version's delivery state to Not delivered with the cause and, for the Trip's latest version, publishes TripNotDelivered in the same transaction. "
                + "A report for a version older than the Trip's latest is stored against that version and publishes nothing. A report for a version that is no longer Sending is ignored and logged: it changes and publishes nothing. "
                + "The X-Correlation-ID is the correlation ID of the Dispatch or send-again, which the TripDispatched event carried. TripNotFound or TripVersionNotFound (404, with that code).");

        trips.MapPost("/{number}/start", StartAsync)
            .WithName("StartTrip")
            .RequireAuthorization(policy => policy.RequireRole(StartRole))
            .WithMetadata(new TrackedFlow(Flows.TripExecution))
            .WithSummary("Start a Dispatched Trip on its day: the Driver reports it from the Driver app.")
            .WithDescription(
                "driverId is the Driver ID from the Driver's token, which the Driver reports Logic App passes on, and the X-Correlation-ID is the Driver's action trail. "
                + "In one transaction the Trip becomes In progress, at the same version, and exactly one TripStarted (Trip number, version, Driver ID, start time) is written to the outbox for the Trip events topic under that correlation ID. "
                + "From then on the only change the Planner can make is taking off a Shipment that isn't collected: every other change is TripUnderWay (409). "
                + "Refused with TripNotFound (404) for an unknown Trip, or one whose Driver is not the Driver from the token, TripNotDispatched (409) for a Draft or a Trip that is In progress already, and NotTripDay (409) when today is not the Trip's day in Norvia's time.");

        trips.MapPost("/{number}/end", EndAsync)
            .WithName("EndTrip")
            .RequireAuthorization(policy => policy.RequireRole(EndRole))
            .WithMetadata(new TrackedFlow(Flows.TripExecution))
            .WithSummary("End an In progress Trip once every Stop has an outcome: the Driver reports it from the Driver app.")
            .WithDescription(
                "driverId is the Driver ID from the Driver's token, which the Driver reports Logic App passes on, and the X-Correlation-ID is the Driver's action trail. "
                + "In one transaction the Trip becomes Completed, at the same version, and exactly one TripCompleted (Trip number, version, Driver ID, end time and the Shipments delivered, not delivered and not collected) is written to the outbox for the Trip events topic under that correlation ID. "
                + "A Shipment that wasn't collected is Planned again and one that was collected but not delivered is flagged Awaiting re-delivery, keeping its status; both leave the Trip, and no ShipmentTakenOffTrip is published. "
                + "Refused with TripNotFound (404) for an unknown Trip, or one whose Driver is not the Driver from the token, TripNotUnderWay (409) for a Trip that isn't In progress, and StopsOpen (409) while a Stop has no outcome. A refusal changes nothing.");

        trips.MapPut("/{number}", ChangeDraftAsync)
            .WithName("ChangeDraftTrip")
            .RequireAuthorization(policy => policy.RequireRole(TransportOrderEndpoints.ManageRole))
            .WithSummary("Pick the Vehicle, Driver, planned start and planned driving duration of a Draft or Dispatched Trip. A Draft can give them back by leaving them out.")
            .WithDescription(
                "What is given replaces what the Draft had. A change that breaks a rule is refused, leaving the Trip as it was, with a problem whose code says which: "
                + "VehicleNotAvailable, DriverNotAvailable, LicenceNotHeld, DrivingHoursExceeded, OutsideShift or DayNotOpen (409), TripUnderWay (409) for a Trip that is In progress, or TripNotFound (404). "
                + "A change to a Dispatched Trip is an Amend: all four must stay given (TripIncomplete, 409), and a change that alters something raises the version and writes one TripDispatched under a new correlation ID, returned in the X-Correlation-ID response header.");

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
        HttpResponse response,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.ShipmentNumbers is not { Count: > 0 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(AddShipmentsRequest.ShipmentNumbers)] = ["Give the numbers of the Shipments to put on the Trip."] });
        }

        var result = await mediator.Send(new AddShipmentsToTripCommand(number, request.ShipmentNumbers, AmendCorrelationId(response)), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors, "The Shipments can't be put on the Trip.");
    }

    private static async Task<Results<Ok<TakeShipmentOffTripResult>, ProblemHttpResult, ValidationProblem>> TakeShipmentOffAsync(
        string number,
        string shipmentNumber,
        TakeShipmentOffRequest request,
        HttpResponse response,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(TakeShipmentOffRequest.Reason)] = ["Taking a Shipment off a Trip needs a reason."] });
        }

        // Like a Dispatch, the action starts in the TMS UI, so the TMS starts its Message trail.
        var result = await mediator.Send(new TakeShipmentOffTripCommand(number, shipmentNumber, request.Reason.Trim(), AmendCorrelationId(response)), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors, "The Shipment can't be taken off the Trip.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> ReorderStopsAsync(
        string number,
        ReorderStopsRequest request,
        HttpResponse response,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.Stops is not { Count: > 0 } stops || !AreStops(stops))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(ReorderStopsRequest.Stops)] = ["Give every Stop of the Trip as a Shipment number and Collection or Delivery."] });
        }

        var result = await mediator.Send(new ReorderTripStopsCommand(number, stops, AmendCorrelationId(response)), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors, "The Stops can't be put in that order.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> ChangeDraftAsync(
        string number,
        ChangeTripRequest request,
        HttpResponse response,
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
            new ChangeTripCommand(number, VehicleOf(request.VehicleId), DriverOf(request.DriverId), request.PlannedStart, request.PlannedDrivingDuration, AmendCorrelationId(response)), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors, "The Trip can't be changed.");
    }

    private static async Task<Results<Ok<DispatchTripResult>, ProblemHttpResult>> DispatchAsync(string number, IMediator mediator, CancellationToken cancellationToken)
    {
        // The action starts in the TMS UI, not at API Management, so the TMS starts its Message trail.
        var result = await mediator.Send(new DispatchTripCommand(number, CorrelationId.New().Value), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors, "The Trip can't be dispatched.");
    }

    private static async Task<Results<Ok<SendTripAgainResult>, ProblemHttpResult>> SendAgainAsync(string number, int tripVersion, IMediator mediator, CancellationToken cancellationToken)
    {
        // Like a Dispatch, the action starts in the TMS UI, so the TMS starts its Message trail.
        var result = await mediator.Send(new SendTripAgainCommand(number, tripVersion, CorrelationId.New().Value), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : Refused(result.ValidationErrors, "The Trip can't be sent again.");
    }

    private static async Task<Results<Ok<TripDeliveryReportResult>, ProblemHttpResult, ValidationProblem>> ReportDeliveredAsync(
        string number,
        int tripVersion,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return TypedResults.ValidationProblem(CorrelationIdMissing("Reporting a Trip's delivery"));
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReportTripDeliveredCommand(number, tripVersion, correlationId), cancellationToken).AsTask());
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ReportRefused(result, correlationId);
    }

    private static async Task<Results<Ok<TripDeliveryReportResult>, ProblemHttpResult, ValidationProblem>> ReportNotDeliveredAsync(
        string number,
        int tripVersion,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        TripNotDeliveredRequest report,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var errors = NotDeliveredErrors(correlationId, report.Cause);
        if (errors.Count > 0 || correlationId is null || report.Cause is not { } cause)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new ReportTripNotDeliveredCommand(number, tripVersion, correlationId, cause), cancellationToken).AsTask());
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ReportRefused(result, correlationId);
    }

    private static async Task<Results<Ok<StartTripResult>, ProblemHttpResult, ValidationProblem>> StartAsync(
        string number,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        StartTripRequest request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var errors = string.IsNullOrWhiteSpace(correlationId) ? CorrelationIdMissing("Starting a Trip") : [];
        if (string.IsNullOrWhiteSpace(request.DriverId))
        {
            errors[nameof(StartTripRequest.DriverId)] = ["Starting a Trip needs the Driver ID from the Driver's token."];
        }

        if (errors.Count > 0 || correlationId is null || request.DriverId is not { } driverId)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new StartTripCommand(number, driverId, correlationId), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<TripRefusalCode, StartTripResult>(
                result, "The Trip can't be started.", correlationId, code => code == TripRefusalCode.TripNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<EndTripResult>, ProblemHttpResult, ValidationProblem>> EndAsync(
        string number,
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        EndTripRequest request,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        var errors = string.IsNullOrWhiteSpace(correlationId) ? CorrelationIdMissing("Ending a Trip") : [];
        if (string.IsNullOrWhiteSpace(request.DriverId))
        {
            errors[nameof(EndTripRequest.DriverId)] = ["Ending a Trip needs the Driver ID from the Driver's token."];
        }

        if (errors.Count > 0 || correlationId is null || request.DriverId is not { } driverId)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await retrier.RunAsync(mediator => mediator.Send(new EndTripCommand(number, driverId, correlationId), cancellationToken).AsTask());
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<TripRefusalCode, EndTripResult>(
                result, "The Trip can't be ended.", correlationId, code => code == TripRefusalCode.TripNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
    }

    // The change may be an Amend of a Dispatched Trip, which starts a Message trail in the TMS UI like a Dispatch does. The caller can't tell
    // beforehand, so the correlation ID is always created and answered in a header; it matters when the Trip was Dispatched.
    private static string AmendCorrelationId(HttpResponse response)
    {
        var correlationId = CorrelationId.New().Value;
        response.Headers[TransportOrderEndpoints.CorrelationIdHeader] = correlationId;
        return correlationId;
    }

    private static Dictionary<string, string[]> NotDeliveredErrors(string? correlationId, string? cause)
    {
        var errors = string.IsNullOrWhiteSpace(correlationId) ? CorrelationIdMissing("Reporting a Trip's delivery") : [];
        if (string.IsNullOrWhiteSpace(cause))
        {
            errors[nameof(TripNotDeliveredRequest.Cause)] = ["A not-delivered report needs the technical cause."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> CorrelationIdMissing(string action) =>
        new() { [TransportOrderEndpoints.CorrelationIdHeader] = [$"{action} needs an {TransportOrderEndpoints.CorrelationIdHeader}."] };

    private static ProblemHttpResult ReportRefused(Ardalis.Result.Result<TripDeliveryReportResult> result, string correlationId) =>
        RefusalProblem.Of<TripRefusalCode, TripDeliveryReportResult>(result, "The report can't be recorded.", correlationId, _ => StatusCodes.Status404NotFound);

    private static async Task<Results<NoContent, ProblemHttpResult>> DiscardDraftAsync(string number, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DiscardDraftTripCommand(number), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Refused(result.ValidationErrors, "The Trip can't be discarded.");
    }

    // A missing shipmentNumber binds as null, whatever the type says.
    private static bool AreStops(IReadOnlyList<StopReference> stops) =>
        stops.All(stop => !string.IsNullOrWhiteSpace(stop.ShipmentNumber) && Enum.IsDefined(stop.Kind));

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
            statusCode: Enum.TryParse<TripRefusalCode>(first.ErrorCode, out var code) && code is TripRefusalCode.TripNotFound or TripRefusalCode.ShipmentNotFound or TripRefusalCode.TripVersionNotFound
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
