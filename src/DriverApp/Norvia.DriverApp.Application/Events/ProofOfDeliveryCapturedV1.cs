using System.Text.Json.Serialization;

namespace Norvia.DriverApp.Application.Events;

/// <summary>
/// The Canonical Model event <c>ProofOfDeliveryCaptured</c>, version 1 (contracts/canonical-model/proof-of-delivery-captured.v1.schema.json). The Driver
/// confirmed the Proof of delivery form at a delivery Stop. The files are too large for Service Bus, so the event carries their blob references and the
/// Driver reports Logic App fetches them from the Driver app's transit container (ADR 0009). It shares its correlation ID with the
/// <c>StopCompletedReported</c> (Delivered) of the same Deliver.
/// </summary>
/// <param name="DocumentId">The Driver app's unique ID for the Proof of delivery: sent again, the TMS recognises a repeat.</param>
/// <param name="DeliveryTime">When the Driver confirmed the form.</param>
/// <param name="Condition">Clean or Reservations, as the Canonical Model names them.</param>
/// <param name="Remark">What the Driver says of the reservations; null, and then left out of the body, when the goods were clean.</param>
public sealed record ProofOfDeliveryCapturedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TripNumber,
    int Version,
    string DriverId,
    string ShipmentNumber,
    string DocumentId,
    string ReceivedBy,
    DateTimeOffset DeliveryTime,
    StopLocationV1 Location,
    string Condition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Remark,
    IReadOnlyList<ProofOfDeliveryFileV1> Files) : DriverAppEvent(EventId, OccurredAt, CorrelationId, TripNumber, Version, DriverId)
{
    public override string EventType => "ProofOfDeliveryCaptured";

    public override int EventVersion => 1;
}
