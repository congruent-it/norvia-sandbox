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
}
