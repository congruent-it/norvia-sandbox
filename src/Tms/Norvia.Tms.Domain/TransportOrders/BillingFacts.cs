namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// What the TMS knows about a Transport Order's Shipment when it checks whether the order is Billable (P9): the facts that
/// aren't the Transport Order's own.
/// </summary>
/// <param name="DeliveredAt">When the Shipment was Delivered; null while it isn't.</param>
/// <param name="HasCurrentProofOfDelivery">Whether the Shipment has a current Proof of delivery (P6).</param>
/// <param name="HasOpenException">Whether the Shipment has an open Exception (P7), whatever its type.</param>
public sealed record BillingFacts(DateTimeOffset? DeliveredAt, bool HasCurrentProofOfDelivery, bool HasOpenException);
