namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// Where a Transport Order stands for billing (P9). It is separate from its status and its rating result, as the rating
/// result is separate from the status in P2.
/// </summary>
public enum BillingState
{
    /// <summary>A Fulfilment Transport Order, billed through the Fulfilment Contract. P9 does nothing more with it. Final.</summary>
    NotBilledSeparately = 1,

    /// <summary>A Transport-only order that doesn't meet the conditions to be Billable yet.</summary>
    NotYetBillable,

    /// <summary>It met the conditions, and <c>TransportOrderBillable</c> was published with its frozen Freight Charge. Final.</summary>
    Billable,
}
