namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// Why the TMS refused a Planner's assignment (P3). The names are stable codes that callers and monitoring rely on:
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
}
