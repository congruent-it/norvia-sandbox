namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// Why the TMS refused Customer service's release of a Transport Order without a Proof of delivery (P9). The names are
/// stable codes that callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum BillingRefusalCode
{
    /// <summary>The TMS has no Transport Order with that number.</summary>
    TransportOrderNotFound = 1,

    /// <summary>The Transport Order is Billable already: Billable is final.</summary>
    AlreadyBillable,

    /// <summary>The Transport Order is a Fulfilment order, billed through the Fulfilment Contract.</summary>
    NotBilledSeparately,

    /// <summary>The Transport Order's Shipment isn't Delivered yet.</summary>
    ShipmentNotDelivered,

    /// <summary>Customer service released the Transport Order without a Proof of delivery already.</summary>
    AlreadyReleased,
}
