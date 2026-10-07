namespace Norvia.Tms.Application.Billing.ListNotYetBillable;

/// <summary>
/// Why a Delivered Transport-only order isn't Billable yet (P9). <c>NotDelivered</c> isn't one: the list holds only orders
/// whose Shipment is Delivered.
/// </summary>
public enum BillingBlockingReason
{
    /// <summary>Its rating result isn't Rated (P2): Not yet rated or Rating failed.</summary>
    NotRated = 1,

    /// <summary>Its Shipment has no current Proof of delivery (P6), and Customer service hasn't released it without one.</summary>
    NoProofOfDelivery,

    /// <summary>Its Shipment has an open Exception (P7), whatever its type.</summary>
    OpenException,
}
