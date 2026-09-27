namespace Norvia.Tms.Domain.Shipments;

public enum ShipmentStatus
{
    /// <summary>Assigned to Own fleet or a Carrier, not yet released. The Planner can still reassign it (P3).</summary>
    Planned,
}
