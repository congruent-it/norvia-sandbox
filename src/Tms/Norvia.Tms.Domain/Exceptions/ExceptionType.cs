namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// What went wrong with a Shipment (P7). A Shipment has at most one open Exception of each type. The names are stable:
/// never rename one, only add.
/// </summary>
public enum ExceptionType
{
    /// <summary>A Delivery failed Tracking event (Carrier code <c>NDL</c>).</summary>
    DeliveryFailed = 1,

    /// <summary>A Collection failed Tracking event (Carrier code <c>NPU</c>).</summary>
    CollectionFailed = 2,

    /// <summary>A Damaged Tracking event (Carrier code <c>DMG</c>).</summary>
    Damaged = 3,

    /// <summary>A Proof of delivery recorded with the condition "with reservations". Apart from Damaged, since reservations are often a shortage.</summary>
    Reservations = 4,

    /// <summary>A Tracking event whose ETA is after the delivery window's latest time, or that time passing, plus the TMS's grace, without the Shipment being Delivered.</summary>
    LateDelivery = 5,
}
