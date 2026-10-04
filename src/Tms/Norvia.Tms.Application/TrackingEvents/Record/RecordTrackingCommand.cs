using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// Records a Tracking event a Carrier reported for a Shipment (P5), in the Canonical Model, and moves the Shipment's
/// status where the event warrants it. The Shipment is the one on the Carrier's Confirmed booking with the Carrier
/// booking reference. The Carrier is the source of the report, which API Management sets from the token (ADR 0005),
/// never the Carrier's own payload. An event that repeats a source's event ID already recorded succeeds as a repeat,
/// changing and publishing nothing. When the event moved the Shipment, the matching event is published in the same
/// transaction.
/// </summary>
/// <param name="Report">
/// What the Carrier reported. Its source is the Carrier, which API Management sets from the token; Own fleet is for P12,
/// which adds its own way of finding the Shipment.
/// </param>
/// <param name="CorrelationId">The event's own Message trail, which API Management creates.</param>
public sealed record RecordTrackingCommand(TrackingReport Report, string CorrelationId)
    : ICommand<Result<RecordTrackingResult>>;
