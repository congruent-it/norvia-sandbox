using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing;

/// <summary>
/// The change of a transaction that can complete a Transport Order's conditions to be Billable (P9): the Shipment moving to
/// Delivered, the Transport Order being Rated, or a Proof of delivery being recorded. What the transaction has done but not
/// saved yet can't be read back, so it is passed in.
/// </summary>
/// <param name="CorrelationId">The Message trail of the transaction, which <c>TransportOrderBillable</c> carries if the change makes the order Billable.</param>
/// <param name="At">When the transaction happens.</param>
/// <param name="DeliveredAt">When the transaction delivered the Shipment; null when it didn't.</param>
/// <param name="ProofOfDeliveryRecorded">Whether the transaction recorded a Proof of delivery for the Shipment.</param>
public sealed record BillingChange(CorrelationId CorrelationId, DateTimeOffset At, DateTimeOffset? DeliveredAt = null, bool ProofOfDeliveryRecorded = false);
