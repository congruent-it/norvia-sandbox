namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// Why the TMS refused a Planner's assignment, reassignment or release (P3). The names are stable codes that callers and monitoring rely on:
/// never rename one, only add.
/// </summary>
public enum AssignmentRefusalCode
{
    /// <summary>No Transport Order has the number.</summary>
    TransportOrderNotFound = 1,

    /// <summary>The Transport Order isn't Accepted, as a Rejected booking is never planned.</summary>
    TransportOrderNotAccepted,

    /// <summary>The Transport Order is Planned already: it has its one Shipment (ADR 0007).</summary>
    TransportOrderAlreadyPlanned,

    /// <summary>The chosen Carrier isn't in the TMS's Carrier data.</summary>
    UnknownCarrier,

    /// <summary>The chosen Carrier is in the TMS's Carrier data, but not active.</summary>
    InactiveCarrier,

    /// <summary>No Shipment has the number.</summary>
    ShipmentNotFound,

    /// <summary>
    /// The Shipment isn't Planned, so it is released already and handed over: it can't be reassigned or released again
    /// by P3.
    /// </summary>
    ShipmentNotPlanned,

    /// <summary>The reassignment names who carries the Shipment already, so there is nothing to change.</summary>
    AssignmentUnchanged,

    /// <summary>The release can't price the Carrier booking, because the Carrier has no Carrier Rate Card (P9).</summary>
    NoRateCard,

    /// <summary>The release can't price the Carrier booking, because no version of the Carrier Rate Card is valid on the collection date (P9).</summary>
    NoValidRateCardVersion,

    /// <summary>The release can't price the Carrier booking, because no Lane of the Carrier Rate Card covers the origin and destination Zones (P9).</summary>
    NoMatchingLane,

    /// <summary>The release can't price the Carrier booking, because the total weight is above the Lane's highest Weight Break (P9).</summary>
    WeightAboveHighestBreak,
}
