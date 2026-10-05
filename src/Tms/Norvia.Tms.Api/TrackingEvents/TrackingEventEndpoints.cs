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
                + "transaction. A late event, or one after Delivered, is stored and moves and publishes nothing. CollectionFailed, DeliveryFailed and Damaged are exceptions: stored, never moving the status, and published as ShipmentExceptionReported with the exception type and remark. The Carrier booking number finds the Shipment when no booking reference is given or it doesn't match. An event sent "
                + "again under the same event ID is answered as before, flagged as a repeat, without recording or publishing anything. An event is "
                + "refused, changing and publishing nothing, with a problem whose code says why: ShipmentNotFound (404), ShipmentNotInTransport "
                + "(409), EventTimeInTheFuture, UnknownEventType or MissingRequiredField (400); and 400 for an input that isn't the Canonical Model.");

        return app;
    }

    private static async Task<Results<Ok<RecordTrackingResult>, ProblemHttpResult>> RecordAsync(
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        TrackingEventRequest trackingEvent,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Invalid(TrackingEventRefusalCode.MissingRequiredField, $"Recording a Tracking event needs an {TransportOrderEndpoints.CorrelationIdHeader}.", string.Empty);
        }

        if (Check(trackingEvent) is { } invalid)
        {
            return Invalid(invalid.Code, invalid.Message, correlationId);
        }

        var result = await mediator.Send(new RecordTrackingCommand(ToReport(trackingEvent), correlationId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : RefusalProblem.Of<TrackingEventRefusalCode, RecordTrackingResult>(result, "The Tracking event can't be recorded.", correlationId, StatusCodeOf);
    }

    private static ProblemHttpResult Invalid(TrackingEventRefusalCode code, string message, string correlationId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Tracking event can't be recorded.",
            detail: message,
            extensions: new Dictionary<string, object?> { ["code"] = code.ToString(), ["correlationId"] = correlationId });

    /// <summary>What the Canonical Model event reports, as the domain takes it: the source is a Carrier or Own fleet. Checked first.</summary>
    private static TrackingReport ToReport(TrackingEventRequest trackingEvent)
    {
        var source = trackingEvent.Source!.Type == TrackingSourceType.Carrier
            ? TrackingSource.Carrier(new CarrierId(trackingEvent.Source.CarrierId!))
            : TrackingSource.OwnFleet;
        var location = trackingEvent.Location!;
        return new TrackingReport(
            source,
            trackingEvent.EventId!,
            trackingEvent.CarrierBookingReference,
            trackingEvent.CarrierBookingNumber,
            Enum.Parse<TrackingEventType>(trackingEvent.Type!),
            trackingEvent.EventTime!.Value,
            new TrackingLocation(location.Place!, location.Postcode!, location.CountryCode!),
            trackingEvent.Eta,
            trackingEvent.Remark);
    }

    /// <summary>The first thing wrong with the event beyond its JSON shape: a field the Canonical Model requires, or a type the TMS doesn't know.</summary>
    private static (TrackingEventRefusalCode Code, string Message)? Check(TrackingEventRequest trackingEvent)
    {
        static (TrackingEventRefusalCode, string) Missing(string message) => (TrackingEventRefusalCode.MissingRequiredField, message);

        if (string.IsNullOrWhiteSpace(trackingEvent.EventId))
        {
            return Missing("A Tracking event needs its source's own event ID.");
        }

        if (trackingEvent.Source is not { } source)
        {
            return Missing("A Tracking event needs its source.");
        }

        if (source.Type == TrackingSourceType.Carrier && string.IsNullOrWhiteSpace(source.CarrierId))
        {
            return Missing("A Carrier source needs the Carrier ID.");
        }

        if (source.Type == TrackingSourceType.OwnFleet && source.CarrierId is not null)
        {
            return Missing("An Own fleet source names no Carrier.");
        }

        if (string.IsNullOrWhiteSpace(trackingEvent.CarrierBookingReference) && string.IsNullOrWhiteSpace(trackingEvent.CarrierBookingNumber))
        {
            return Missing("A Tracking event needs the Carrier's own booking reference, or else the Carrier booking number.");
        }

        if (string.IsNullOrWhiteSpace(trackingEvent.Type))
        {
            return Missing("A Tracking event needs its type.");
        }

        if (!Enum.TryParse<TrackingEventType>(trackingEvent.Type, out var type) || !Enum.IsDefined(type) || trackingEvent.Type.All(char.IsAsciiDigit))
        {
            return (TrackingEventRefusalCode.UnknownEventType, $"'{trackingEvent.Type}' isn't a Tracking event type the TMS knows.");
        }

        if (trackingEvent.EventTime is null)
        {
            return Missing("A Tracking event needs the time it happened.");
        }

        if (trackingEvent.Location is not { } location || string.IsNullOrWhiteSpace(location.Place) || string.IsNullOrWhiteSpace(location.Postcode))
        {
            return Missing("A location needs its place and postcode.");
        }

        if (location.CountryCode is not { Length: 2 } || !location.CountryCode.All(char.IsAsciiLetterUpper))
        {
            return Missing("A location needs its country as an ISO 3166-1 alpha-2 code.");
        }

        return null;
    }

    private static int StatusCodeOf(TrackingEventRefusalCode code) => code switch
    {
        TrackingEventRefusalCode.ShipmentNotFound => StatusCodes.Status404NotFound,
        TrackingEventRefusalCode.EventTimeInTheFuture or TrackingEventRefusalCode.UnknownEventType or TrackingEventRefusalCode.MissingRequiredField => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict, // ShipmentNotInTransport
    };
}
