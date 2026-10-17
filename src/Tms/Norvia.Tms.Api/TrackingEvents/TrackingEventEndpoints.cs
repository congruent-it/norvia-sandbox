using System.Diagnostics.CodeAnalysis;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TrackingEvents.Record;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Api.TrackingEvents;

public static class TrackingEventEndpoints
{
    /// <summary>
    /// The app role of the callers that record Tracking events: the Carrier-tracking Logic App's managed identity, and P12's
    /// Driver reports Logic App's.
    /// </summary>
    public const string RecordRole = "TrackingEvents.Record";

    public static IEndpointRouteBuilder MapTrackingEvents(this IEndpointRouteBuilder app)
    {
        var trackingEvents = app.NewVersionedApi("TrackingEvents")
            .MapGroup("/v{version:apiVersion}/tracking-events")
            .HasApiVersion(1);

        trackingEvents.MapPost("/", RecordAsync)
            .WithName("RecordTrackingEvent")
            .RequireAuthorization(policy => policy.RequireRole(RecordRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierTracking))
            .WithSummary("Record a Tracking event a Carrier or a Driver reported for a Shipment.")
            .WithDescription(
                "Takes only the Canonical Model. The event names its source: a Carrier ID (from API Management, never from the Carrier's payload) "
                + "or Own fleet with the Driver ID (from the Driver's token, which the Driver reports Logic App passes on, never from the Driver app's payload). "
                + "A Carrier's Shipment is the one on that Carrier's Confirmed Carrier booking with the Carrier booking reference. An Own fleet event names its Trip, "
                + "the Shipment on it and the version of the Trip the Driver app held (kept for tracing only) in place of a booking reference, and the X-Correlation-ID is the Driver's action trail. "
                + "A Driver reports Collected, CollectionFailed, Delivered, DeliveryFailed and Damaged. Collected and Delivered move the Shipment Dispatched, Collected, Delivered; "
                + "CollectionFailed, DeliveryFailed and Damaged change no status and open the matching Exception (P7). Collected and Collection failed are the outcomes of the Shipment's collection Stop and "
                + "Delivered and Delivery failed of its delivery Stop, each final on its Trip; Damaged is no outcome. The TMS "
                + "stores the event with the correlation ID and the source's event ID. The Shipment's status only moves forward, and steps "
                + "can be skipped: Collected moves it to Collected, Departed, Arrived and OutForDelivery to InTransit, Delivered to Delivered, "
                + "publishing ShipmentCollected, ShipmentInTransit or ShipmentDelivered (only the one for the status landed on) in the same "
                + "transaction. A late event, or one after Delivered, is stored and moves and publishes nothing. CollectionFailed, DeliveryFailed and Damaged are exceptions: stored, never moving the status, and published as ShipmentExceptionReported with the exception type and remark. The Carrier booking number finds the Shipment when no booking reference is given or it doesn't match. An event sent "
                + "again under the same event ID is answered as before, flagged as a repeat, without recording or publishing anything. An event is "
                + "refused, changing and publishing nothing, with a problem whose code says why: ShipmentNotFound (404), ShipmentNotInTransport "
                + "(409), EventTimeInTheFuture, UnknownEventType or MissingRequiredField (400); and for an Own fleet event TripNotFound (404, also for another Driver's Trip), "
                + "TripNotUnderWay, ShipmentNotOnTrip (checked against the Trip's latest version, even when the event names an older one), StopAlreadyCompleted (Damaged is exempt) or CollectionBeforeDelivery (409); and 400 for an input that isn't the Canonical Model.");

        return app;
    }

    private static async Task<Results<Ok<RecordTrackingResult>, ProblemHttpResult>> RecordAsync(
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        TrackingEventRequest trackingEvent,
        IConflictRetrier retrier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Invalid([Missing($"Recording a Tracking event needs an {TransportOrderEndpoints.CorrelationIdHeader}.")], string.Empty);
        }

        if (!TryRead(trackingEvent, out var report, out var problems))
        {
            return Invalid(problems, correlationId);
        }

        // A Carrier's events for one Shipment can arrive together, and the save of the one that loses the race is run once more.
        var result = await retrier.RunAsync(mediator => mediator.Send(new RecordTrackingCommand(report, correlationId), cancellationToken).AsTask());

        return result.IsSuccess ? TypedResults.Ok(result.Value) : RefusalProblem.Of<TrackingEventRefusalCode, RecordTrackingResult>(result, "The Tracking event can't be recorded.", correlationId, StatusCodeOf);
    }

    /// <summary>
    /// A 400 for an event the TMS can't read. Its <c>code</c> is the first problem's, and <c>errors</c> lists every
    /// problem with its own code and message.
    /// </summary>
    private static ProblemHttpResult Invalid(IReadOnlyList<ReadProblem> problems, string correlationId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Tracking event can't be recorded.",
            detail: problems[0].Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = problems[0].Code.ToString(),
                ["correlationId"] = correlationId,
                ["errors"] = problems.Select(problem => new { code = problem.Code.ToString(), message = problem.Message }).ToArray(),
            });

    private static ReadProblem Missing(string message) => new(TrackingEventRefusalCode.MissingRequiredField, message);

    /// <summary>
    /// Reads the Canonical Model event as the domain takes it, beyond its JSON shape: the fields it requires, and a type
    /// the TMS knows. Either returns the report, or every problem found, in the order of the event's fields.
    /// </summary>
    private static bool TryRead(
        TrackingEventRequest trackingEvent,
        [NotNullWhen(true)] out TrackingReport? report,
        out IReadOnlyList<ReadProblem> problems)
    {
        var found = new List<ReadProblem>();
        report = null;
        problems = found;

        if (string.IsNullOrWhiteSpace(trackingEvent.EventId))
        {
            found.Add(Missing("A Tracking event needs its source's own event ID."));
        }

        TrackingSource? source = null;
        OwnFleetReport? ownFleet = null;
        if (trackingEvent.Source is not { } sourceRequest)
        {
            found.Add(Missing("A Tracking event needs its source."));
        }
        else if (sourceRequest.Type == TrackingSourceType.Carrier)
        {
            if (string.IsNullOrWhiteSpace(sourceRequest.CarrierId))
            {
                found.Add(Missing("A Carrier source needs the Carrier ID."));
            }
            else
            {
                source = TrackingSource.Carrier(new CarrierId(sourceRequest.CarrierId));
            }

            if (sourceRequest.DriverId is not null || trackingEvent.TripNumber is not null || trackingEvent.ShipmentNumber is not null || trackingEvent.TripVersion is not null)
            {
                found.Add(Missing("A Carrier event names no Driver, Trip or Shipment."));
            }

            if (string.IsNullOrWhiteSpace(trackingEvent.CarrierBookingReference) && string.IsNullOrWhiteSpace(trackingEvent.CarrierBookingNumber))
            {
                found.Add(Missing("A Tracking event needs the Carrier's own booking reference, or else the Carrier booking number."));
            }
        }
        else if (sourceRequest.CarrierId is not null)
        {
            found.Add(Missing("An Own fleet source names no Carrier."));
        }
        else
        {
            source = TrackingSource.OwnFleet;
            ownFleet = ReadOwnFleet(trackingEvent, sourceRequest, found);
        }

        TrackingEventType? type = null;
        if (string.IsNullOrWhiteSpace(trackingEvent.Type))
        {
            found.Add(Missing("A Tracking event needs its type."));
        }
        else if (Enum.TryParse<TrackingEventType>(trackingEvent.Type, out var parsed) && Enum.IsDefined(parsed) && !trackingEvent.Type.All(char.IsAsciiDigit))
        {
            type = parsed;
        }
        else
        {
            found.Add(new(TrackingEventRefusalCode.UnknownEventType, $"'{trackingEvent.Type}' isn't a Tracking event type the TMS knows."));
        }

        if (trackingEvent.EventTime is null)
        {
            found.Add(Missing("A Tracking event needs the time it happened."));
        }

        TrackingLocation? place = null;
        if (trackingEvent.Location is not { } location || string.IsNullOrWhiteSpace(location.Place) || string.IsNullOrWhiteSpace(location.Postcode))
        {
            found.Add(Missing("A location needs its place and postcode."));
        }
        else if (location.CountryCode is not { Length: 2 } || !location.CountryCode.All(char.IsAsciiLetterUpper))
        {
            found.Add(Missing("A location needs its country as an ISO 3166-1 alpha-2 code."));
        }
        else
        {
            place = new TrackingLocation(location.Place, location.Postcode, location.CountryCode);
        }

        var ownFleetMissing = source is { IsOwnFleet: true } && ownFleet is null;
        if (found.Count > 0 || ownFleetMissing || trackingEvent.EventId is not { } eventId || source is null || type is not { } knownType || trackingEvent.EventTime is not { } eventTime || place is null)
        {
            return false;
        }

        report = new TrackingReport(
            source,
            eventId,
            trackingEvent.CarrierBookingReference,
            trackingEvent.CarrierBookingNumber,
            knownType,
            eventTime,
            place,
            trackingEvent.Eta,
            trackingEvent.Remark,
            ownFleet);
        return true;
    }

    // An Own fleet event is about a Trip, a Shipment on it and a Driver, and names no Carrier booking.
    private static OwnFleetReport? ReadOwnFleet(TrackingEventRequest trackingEvent, TrackingSourceRequest source, List<ReadProblem> found)
    {
        var before = found.Count;
        if (!TripNumber.TryParse(trackingEvent.TripNumber, out var tripNumber))
        {
            found.Add(Missing("An Own fleet event needs the number of its Trip, such as TR-0000042."));
        }

        if (!ShipmentNumber.TryParse(trackingEvent.ShipmentNumber, out var shipmentNumber))
        {
            found.Add(Missing("An Own fleet event needs the number of its Shipment, such as SH-0000042."));
        }

        if (trackingEvent.TripVersion is not >= 0)
        {
            found.Add(Missing("An Own fleet event needs the version of the Trip the Driver app held."));
        }

        if (string.IsNullOrWhiteSpace(source.DriverId))
        {
            found.Add(Missing("An Own fleet source needs the Driver ID from the Driver's token."));
        }

        if (trackingEvent.CarrierBookingReference is not null || trackingEvent.CarrierBookingNumber is not null)
        {
            found.Add(Missing("An Own fleet event names no Carrier booking."));
        }

        return found.Count > before || tripNumber is null || shipmentNumber is null || source.DriverId is not { } driverId || trackingEvent.TripVersion is not { } version
            ? null
            : new OwnFleetReport(tripNumber, new DriverId(driverId), shipmentNumber, version);
    }

    /// <summary>One thing wrong with an event the TMS can't read, with the stable code of its kind.</summary>
    private readonly record struct ReadProblem(TrackingEventRefusalCode Code, string Message);

    private static int StatusCodeOf(TrackingEventRefusalCode code) => code switch
    {
        TrackingEventRefusalCode.ShipmentNotFound or TrackingEventRefusalCode.TripNotFound => StatusCodes.Status404NotFound,
        TrackingEventRefusalCode.EventTimeInTheFuture or TrackingEventRefusalCode.UnknownEventType or TrackingEventRefusalCode.MissingRequiredField => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict, // ShipmentNotInTransport, and the Trip rules of an Own fleet event
    };
}
