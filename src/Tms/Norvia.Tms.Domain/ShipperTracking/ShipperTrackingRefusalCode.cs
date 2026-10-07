namespace Norvia.Tms.Domain.ShipperTracking;

/// <summary>
/// Why the TMS refused a Shipper's tracking query (P8). The names are stable codes that callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum ShipperTrackingRefusalCode
{
    /// <summary>The Transport Order doesn't exist or belongs to another Shipper, the same answer for both so the API doesn't reveal whose it is.</summary>
    TransportOrderNotFound = 1,

    /// <summary>The Transport Order has no current Proof of delivery, or the file isn't one of its files: a superseded Proof of delivery's file is not found, as if it never existed.</summary>
    ProofOfDeliveryNotFound,
}
