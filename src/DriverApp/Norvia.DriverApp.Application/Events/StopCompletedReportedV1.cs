using System.Text.Json.Serialization;

namespace Norvia.DriverApp.Application.Events;

/// <summary>Where a Stop report happened: the Stop's address, as the Canonical Model's Tracking event wants it. The Driver app doesn't read GPS.</summary>
public sealed record StopLocationV1(string Place, string Postcode, string CountryCode);

/// <summary>
/// The Canonical Model event <c>StopCompletedReported</c>, version 1 (contracts/canonical-model/stop-completed-reported.v1.schema.json). The Driver reported
/// on a Stop: Collected, Collection failed or Delivery failed completes it, Damaged doesn't. The Driver app reports and the TMS decides, so it says what
/// the Driver did, not what the Stop's outcome is.
/// </summary>
/// <param name="Stop">Collection or Delivery, as the schema names them.</param>
/// <param name="TrackingEventType">The Tracking event (P5) to record: Collected, CollectionFailed, Delivered, DeliveryFailed or Damaged.</param>
/// <param name="Remark">What the Driver said; null when they gave none, and then left out of the body.</param>
public sealed record StopCompletedReportedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TripNumber,
    int Version,
    string DriverId,
    string ShipmentNumber,
    string Stop,
    string TrackingEventType,
    StopLocationV1 Location,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Remark) : DriverAppEvent(EventId, OccurredAt, CorrelationId, TripNumber, Version, DriverId)
{
    public override string EventType => "StopCompletedReported";

    public override int EventVersion => 1;
}
