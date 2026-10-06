using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

/// <summary>
/// The Canonical Model event <c>ShipmentDeliveryWindowAgreed</c>, version 1 (contracts/canonical-model/shipment-delivery-window-agreed.v1.schema.json).
/// The Planner agreed a delivery window with the Carrier for a Collected or In transit Shipment (P7), under the agreement's own
/// correlation ID.
/// </summary>
/// <param name="OccurredAt">When the Planner agreed the window.</param>
/// <param name="Earliest">The agreed window's earliest time.</param>
/// <param name="Latest">The agreed window's latest time.</param>
/// <param name="ExceptionNumber">The Exception the Planner set it from.</param>
public sealed record ShipmentDeliveryWindowAgreedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    DateTimeOffset Earliest,
    DateTimeOffset Latest,
    string ExceptionNumber)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipmentDeliveryWindowAgreed";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipmentDeliveryWindowAgreedV1 From(ShipmentDeliveryWindowAgreedEvent agreed)
    {
        ArgumentNullException.ThrowIfNull(agreed);
        var window = agreed.Agreed;
        return new(
            Guid.CreateVersion7(),
            window.AgreedAt,
            window.CorrelationId.Value,
            agreed.Shipment.Number.Value,
            agreed.Shipment.TransportOrderNumber.Value,
            window.Window.Earliest!.Value,
            window.Window.Latest!.Value,
            window.ExceptionNumber);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.ExceptionHandling),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
    ];
}
