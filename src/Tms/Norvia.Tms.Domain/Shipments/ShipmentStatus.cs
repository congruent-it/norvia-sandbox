namespace Norvia.Tms.Domain.Shipments;

public enum ShipmentStatus
{
    /// <summary>Assigned to Own fleet or a Carrier, not yet released. The Planner can still reassign it (P3).</summary>
    Planned,

    /// <summary>
    /// Handed over: a Carrier Shipment to P4 for booking, an Own fleet Shipment to P11 for a Trip. P3 can no longer change
    /// it.
    /// </summary>
    Released,

    /// <summary>Its Carrier booking is Confirmed (P4). P5 takes it from here. P3 and P4 can no longer change it.</summary>
    Booked,

    /// <summary>Its Trip is Dispatched (P11): the Own fleet counterpart of Booked. P5 takes it from here.</summary>
    Dispatched,

    /// <summary>The goods have been picked up (P5): a Tracking event reported it. The status only moves forward.</summary>
    Collected,

    /// <summary>The goods are on their way to the delivery address (P5): a Tracking event said Departed, Arrived or Out for delivery.</summary>
    InTransit,

    /// <summary>The goods have been delivered (P5). Final for P5: later Tracking events are logged and change nothing.</summary>
    Delivered,
}
