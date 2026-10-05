namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// What a Tracking event reports (P5). Only the types the TMS can record so far are here; each later P5 ticket adds its own
/// (the exceptions). The names are stable: never rename one, only add.
/// </summary>
public enum TrackingEventType
{
    /// <summary>The goods have been picked up. Moves a Booked Shipment to Collected.</summary>
    Collected = 1,

    /// <summary>The goods have left a depot or stop on the way. Moves the Shipment to In transit.</summary>
    Departed,

    /// <summary>The goods have arrived at a depot or stop on the way. Moves the Shipment to In transit.</summary>
    Arrived,

    /// <summary>The goods are out for delivery to the Consignee. Moves the Shipment to In transit.</summary>
    OutForDelivery,

    /// <summary>The goods have been delivered. Moves the Shipment to Delivered.</summary>
    Delivered,
}
