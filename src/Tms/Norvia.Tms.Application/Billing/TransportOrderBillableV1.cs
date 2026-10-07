using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing;

/// <summary>
/// The Canonical Model event <c>TransportOrderBillable</c>, version 1 (contracts/canonical-model/transport-order-billable.v1.schema.json).
/// Its correlation ID is that of the transaction that made the order Billable; the intake's travels with it.
/// </summary>
public sealed record TransportOrderBillableV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string IntakeCorrelationId,
    string TransportOrderNumber,
    string ShipperId,
    string ShipperReference,
    string ShipmentNumber,
    DateTimeOffset DeliveryTime,
    FreightChargeView FreightCharge,
    bool ReleasedWithoutProofOfDelivery)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TransportOrderBillable";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TransportOrderEvents;

    public static TransportOrderBillableV1 From(TransportOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order is not
            {
                BillingState: BillingState.Billable,
                Number: { } number,
                ShipperReference: { } shipperReference,
                ShipmentNumber: { } shipmentNumber,
                DeliveredAt: { } deliveredAt,
                FreightCharge: { } freightCharge,
                BecameBillableAt: { } becameBillableAt,
                BillingCorrelationId: { } billingCorrelationId,
            })
        {
            throw new ArgumentException("Only a Billable Transport Order is published as Billable.", nameof(order));
        }

        return new(
            Guid.CreateVersion7(),
            becameBillableAt,
            billingCorrelationId.Value,
            order.CorrelationId.Value,
            number.Value,
            order.ShipperId.Value,
            shipperReference,
            shipmentNumber.Value,
            deliveredAt,
            FreightChargeView.From(freightCharge)!,
            order.ReleasedWithoutProofOfDelivery);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.Billing),
        new(TrackingKeys.PartyId, ShipperId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
    ];
}
