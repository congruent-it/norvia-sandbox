using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.Shipments;

/// <param name="Previous">The assignment the reassignment replaced, which says who carried the Shipment before.</param>
public sealed class ShipmentReassignedEvent(Shipment shipment, ShipmentAssignment previous, ShipmentAssignment reassignment) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public ShipmentAssignment Previous { get; } = previous;

    public ShipmentAssignment Reassignment { get; } = reassignment;
}
