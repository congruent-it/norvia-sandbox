namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// Why the TMS refused a Planner's action on an Exception (P7), for the rules that have a stable code. The names are
/// stable codes that callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum ExceptionRefusalCode
{
    /// <summary>The TMS has no Exception with that number.</summary>
    ExceptionNotFound = 1,

    /// <summary>The Exception is resolved already.</summary>
    ExceptionNotOpen,

    /// <summary>The Shipment isn't Collected or In transit, so there is no redelivery to agree a window for.</summary>
    ShipmentNotInTransport,

    /// <summary>The delivery window lacks a time, ends before it starts, or ends in the past.</summary>
    DeliveryWindowInvalid,
}
