using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing;

/// <summary>
/// The TMS Billing module (P9): checks whether a Transport Order is Billable in the same transaction as each change that can
/// complete its conditions, never from a Service Bus event (ADR 0003). When the last condition is met, the order becomes
/// Billable and raises <c>TransportOrderBillable</c>; the caller's save commits both with the change, and the event goes to
/// the outbox in that transaction (ADR 0012).
/// </summary>
public sealed class BillingCheck(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IReadRepository<ExceptionCase> exceptions)
{
    /// <summary>
    /// Checks the Transport Order the Shipment carries out, as the transaction that changed the Shipment would. Reads the
    /// order tracked, so the caller's save keeps its change.
    /// </summary>
    public async Task CheckAsync(Shipment shipment, BillingChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        var order = await transportOrders.SingleOrDefaultAsync(new TransportOrderToBillSpec(shipment.TransportOrderNumber), cancellationToken);
        if (order is not null)
        {
            await CheckAsync(order, shipment, change, cancellationToken);
        }
    }

    /// <summary>
    /// Checks a tracked Transport Order. <paramref name="shipment"/> is its Shipment when the caller has it; null when the
    /// check has to look for it.
    /// </summary>
    public async Task CheckAsync(TransportOrder order, Shipment? shipment, BillingChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(change);

        // Saves reading the Shipment's facts for an order they can't decide for.
        if (!order.AwaitsBillingFacts)
        {
            return;
        }

        shipment ??= await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(order.ShipmentNumber!), cancellationToken);
        if (shipment is null)
        {
            return;
        }

        var deliveredAt = change.DeliveredAt ?? await DeliveredAtAsync(shipment, cancellationToken);
        if (deliveredAt is null)
        {
            return;
        }

        var hasProofOfDelivery = change.ProofOfDeliveryRecorded
            || await proofsOfDelivery.AnyAsync(new CurrentProofOfDeliverySpec(shipment.Id), cancellationToken);
        var hasOpenException = change.ExceptionOpened
            || await exceptions.AnyAsync(new OpenExceptionOfShipmentSpec(shipment.Id, change.ExceptionResolved), cancellationToken);
        order.TryBecomeBillable(new BillingFacts(deliveredAt, hasProofOfDelivery, hasOpenException), change.CorrelationId, change.At);
    }

    private async Task<DateTimeOffset?> DeliveredAtAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        if (shipment.Status != ShipmentStatus.Delivered)
        {
            return null;
        }

        var delivered = await trackingEvents.ListAsync(new DeliveredTrackingEventsForShipmentsSpec([shipment.Id]), cancellationToken);
        return delivered.Count > 0 ? delivered[0].EventTime : null;
    }
}
