using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TrackingEvents.Record;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Api.TrackingEvents;

public static class TrackingEventEndpoints
{
    /// <summary>
    /// The app role of the callers that record Tracking events: the Carrier-tracking Logic App's managed identity, and later
    /// P12's Driver reports Logic App's.
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
            .WithSummary("Record a Tracking event a Carrier reported for a Shipment.")
            .WithDescription(
                "Takes only the Canonical Model. The event names its source: a Carrier ID (from API Management, never from the Carrier's payload) "
                + "or Own fleet. The Shipment is the one on that Carrier's Confirmed Carrier booking with the Carrier booking reference. The TMS "
                + "stores the event with the correlation ID and the source's event ID. The Shipment's status only moves forward, and steps "
                + "can be skipped: Collected moves it to Collected, Departed, Arrived and OutForDelivery to InTransit, Delivered to Delivered, "
                + "publishing ShipmentCollected, ShipmentInTransit or ShipmentDelivered (only the one for the status landed on) in the same "
                + "transaction. A late event, or one after Delivered, is stored and moves and publishes nothing. An event sent "
                + "again under the same event ID is answered as before, flagged as a repeat, without recording or publishing anything. An event is "
                + "refused, changing and publishing nothing, with a problem whose code says why: ShipmentNotFound (404), ShipmentNotInTransport "
                + "(409) or EventTimeInTheFuture (400); and 400 for an input that isn't the Canonical Model.");

        return app;
    }

    private static async Task<Results<Ok<RecordTrackingResult>, ProblemHttpResult, ValidationProblem>> RecordAsync(
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        TrackingEventRequest trackingEvent,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = Check(correlationId, trackingEvent);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new RecordTrackingCommand(ToReport(trackingEvent), correlationId!), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : RefusalProblem.Of<TrackingEventRefusalCode, RecordTrackingResult>(result, "The Tracking event can't be recorded.", correlationId!, StatusCodeOf);
    }

    /// <summary>What the Canonical Model event reports, as the domain takes it: the source is a Carrier or Own fleet.</summary>
    private static TrackingReport ToReport(TrackingEventRequest trackingEvent)
    {
        var source = trackingEvent.Source.Type == TrackingSourceType.Carrier
            ? TrackingSource.Carrier(new CarrierId(trackingEvent.Source.CarrierId!))
            : TrackingSource.OwnFleet;
        var location = trackingEvent.Location;
        return new TrackingReport(
            source,
            trackingEvent.EventId,
            trackingEvent.CarrierBookingReference,
            trackingEvent.Type,
            trackingEvent.EventTime,
            new TrackingLocation(location.Place, location.Postcode, location.CountryCode),
            trackingEvent.Eta,
            trackingEvent.Remark);
    }

    /// <summary>The call's errors: the <see cref="TransportOrderEndpoints.CorrelationIdHeader"/>, and the fields the Canonical Model requires beyond their type.</summary>
    private static Dictionary<string, string[]> Check(string? correlationId, TrackingEventRequest trackingEvent)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[TransportOrderEndpoints.CorrelationIdHeader] = [$"Recording a Tracking event needs an {TransportOrderEndpoints.CorrelationIdHeader}."];
        }

        if (string.IsNullOrWhiteSpace(trackingEvent.EventId))
        {
            errors[nameof(TrackingEventRequest.EventId)] = ["A Tracking event needs its source's own event ID."];
        }

        if (string.IsNullOrWhiteSpace(trackingEvent.CarrierBookingReference))
        {
            errors[nameof(TrackingEventRequest.CarrierBookingReference)] = ["A Tracking event needs the Carrier's own booking reference."];
        }

        var source = trackingEvent.Source;
        if (source.Type == TrackingSourceType.Carrier && string.IsNullOrWhiteSpace(source.CarrierId))
        {
            errors[$"{nameof(TrackingEventRequest.Source)}.{nameof(TrackingSourceRequest.CarrierId)}"] = ["A Carrier source needs the Carrier ID."];
        }
        else if (source.Type == TrackingSourceType.OwnFleet && source.CarrierId is not null)
        {
            errors[$"{nameof(TrackingEventRequest.Source)}.{nameof(TrackingSourceRequest.CarrierId)}"] = ["An Own fleet source names no Carrier."];
        }

        var location = trackingEvent.Location;
        if (string.IsNullOrWhiteSpace(location.Place) || string.IsNullOrWhiteSpace(location.Postcode))
        {
            errors[nameof(TrackingEventRequest.Location)] = ["A location needs its place and postcode."];
        }

        if (location.CountryCode is not { Length: 2 } || !location.CountryCode.All(char.IsAsciiLetterUpper))
        {
            errors[$"{nameof(TrackingEventRequest.Location)}.{nameof(TrackingLocationRequest.CountryCode)}"] = ["A location needs its country as an ISO 3166-1 alpha-2 code."];
        }

        return errors;
    }

    private static int StatusCodeOf(TrackingEventRefusalCode code) => code switch
    {
        TrackingEventRefusalCode.ShipmentNotFound => StatusCodes.Status404NotFound,
        TrackingEventRefusalCode.EventTimeInTheFuture => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict, // ShipmentNotInTransport
    };
}
