using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// Records a Tracking event a Carrier or a Driver reported for a Shipment (P5, P12), in the Canonical Model, and moves the
/// Shipment's status where the event warrants it. A Carrier's event names the Shipment by the Carrier booking reference
/// of its Confirmed booking, and the Carrier is the source of the report, which API Management sets from the token
/// (ADR 0005), never the Carrier's own payload. A Driver's event names its Trip and Shipment, and the Driver ID, which
/// the Driver reports Logic App takes from the Driver's token, decides whether the Trip is theirs. An event that repeats a source's event ID already recorded succeeds as a repeat,
/// changing and publishing nothing. When the event moved the Shipment, the matching event is published in the same
/// transaction.
/// </summary>
/// <param name="Report">What the Carrier or the Driver reported; Own fleet reports carry <see cref="TrackingReport.OwnFleet"/>.</param>
/// <param name="CorrelationId">The event's own Message trail, which API Management creates for a Carrier's and the Driver app for a Driver's.</param>
public sealed record RecordTrackingCommand(TrackingReport Report, string CorrelationId)
    : ICommand<Result<RecordTrackingResult>>;
