using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// Records a Tracking event a Carrier reported for a Shipment (P5), in the Canonical Model, and moves the Shipment's
/// status where the event warrants it. The Shipment is the one on the Carrier's Confirmed booking with the Carrier
/// booking reference. The Carrier is the <paramref name="Source"/>, which API Management sets from the token (ADR 0005),
/// never the Carrier's own payload. An event that repeats a source's event ID already recorded succeeds as a repeat,
/// changing and publishing nothing. When the event moved the Shipment, the matching event is published in the same
/// transaction.
/// </summary>
/// <param name="Source">Who reported the event. Own fleet is for P12, which adds its own way of finding the Shipment.</param>
/// <param name="SourceEventId">The reporter's own ID for the event.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking the event is about.</param>
/// <param name="CorrelationId">The event's own Message trail, which API Management creates.</param>
public sealed record RecordTrackingCommand(
    TrackingSource Source,
    string SourceEventId,
    string CarrierBookingReference,
    TrackingEventType Type,
    DateTimeOffset EventTime,
    TrackingLocation Location,
    DateTimeOffset? Eta,
    string? Remark,
    string CorrelationId)
    : ICommand<Result<RecordTrackingResult>>;
