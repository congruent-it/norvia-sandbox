using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

/// <summary>Where the Transport Order of a Shipment stands for billing (P9), as its timeline shows it.</summary>
/// <param name="BecameBillableAt">When the Transport Order became Billable; null until then.</param>
/// <param name="ProofOfDeliveryRelease">Who released it without a Proof of delivery, when and why; null unless it was released.</param>
public sealed record ShipmentBillingView(
    BillingState BillingState,
    DateTimeOffset? BecameBillableAt,
    ProofOfDeliveryReleaseView? ProofOfDeliveryRelease)
{
    public static ShipmentBillingView? Of(TransportOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.BillingState is { } state ? new(state, order.BecameBillableAt, ProofOfDeliveryReleaseView.From(order)) : null;
    }
}
