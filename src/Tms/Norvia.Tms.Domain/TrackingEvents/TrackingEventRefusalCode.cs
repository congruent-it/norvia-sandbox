namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// Why the TMS refused a Tracking event (P5), for the rules that have a stable code. The names are stable codes that
/// callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum TrackingEventRefusalCode
{
    /// <summary>
    /// No Confirmed Carrier booking of the reporting Carrier has the Carrier booking reference: it is unknown, belongs to
    /// another Carrier, or ended without being carried out.
    /// </summary>
    ShipmentNotFound = 1,

    /// <summary>The Shipment isn't Booked or later, so there is nothing yet for a Carrier to report on.</summary>
    ShipmentNotInTransport,

    /// <summary>The event time is in the future, beyond the clock-skew allowance.</summary>
    EventTimeInTheFuture,

    /// <summary>The event's type isn't one the TMS knows. A client error.</summary>
    UnknownEventType,

    /// <summary>The event lacks a field the Canonical Model requires. A client error.</summary>
    MissingRequiredField,
}
