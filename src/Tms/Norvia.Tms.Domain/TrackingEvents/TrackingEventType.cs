namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// What a Tracking event reports (P5). Progress types move the Shipment's status; exception types never do. The names are stable: never rename one, only add.
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

    /// <summary>The Carrier couldn't collect the goods. An exception: it never changes the status.</summary>
    CollectionFailed,

    /// <summary>The Carrier couldn't deliver the goods. An exception: it never changes the status.</summary>
    DeliveryFailed,

    /// <summary>The goods were damaged on the way. An exception: it never changes the status.</summary>
    Damaged,
}
