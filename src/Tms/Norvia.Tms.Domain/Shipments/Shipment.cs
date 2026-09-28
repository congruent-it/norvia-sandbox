using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// The physical movement that carries out one Transport Order. Each Transport Order has exactly one Shipment, and a
/// Shipment never carries more than one Transport Order (ADR 0007). The TMS owns it. It is made only by planning its
/// Transport Order (<see cref="TransportOrder.Plan"/>), and never deleted. While it is Planned, the Planner can
/// reassign it; it keeps its Shipment number and records each assignment in its history.
/// </summary>
public sealed class Shipment : EntityBase<ShipmentId>, IAggregateRoot
{
    private Shipment(
        ShipmentId id,
        ShipmentNumber number,
        TransportOrderId transportOrderId,
        TransportOrderNumber transportOrderNumber,
        ShipperId shipperId,
        CorrelationId correlationId,
        DateTimeOffset plannedAt)
    {
        Id = id;
        Number = number;
        TransportOrderId = transportOrderId;
        TransportOrderNumber = transportOrderNumber;
        ShipperId = shipperId;
        CorrelationId = correlationId;
        PlannedAt = plannedAt;
    }

    public ShipmentNumber Number { get; private set; }

    public TransportOrderId TransportOrderId { get; private set; }

    public TransportOrderNumber TransportOrderNumber { get; private set; }

    /// <summary>The Shipper of the Transport Order the Shipment carries out.</summary>
    public ShipperId ShipperId { get; private set; }

    public ShipmentStatus Status { get; private set; }

    /// <summary>Who carries the Shipment now: the last of its <see cref="Assignments"/>.</summary>
    public CarriedBy CarriedBy { get; private set; } = CarriedBy.OwnFleet;

    /// <summary>The Shipment's history of assignments, in order: the planning's first, then each reassignment's.</summary>
    public IReadOnlyList<ShipmentAssignment> Assignments { get; private set; } = [];

    /// <summary>
    /// The Message trail of the Planner's assignment, which starts in the TMS UI and so gets a trail of its own, not the
    /// intake's. Each reassignment's trail is in <see cref="Assignments"/>.
    /// </summary>
    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset PlannedAt { get; private set; }

    internal static Shipment Plan(
        ShipmentNumber number,
        TransportOrder order,
        CarriedBy carriedBy,
        CorrelationId correlationId,
        DateTimeOffset plannedAt)
    {
        var shipment = new Shipment(ShipmentId.New(), number, order.Id, order.Number!, order.ShipperId, correlationId, plannedAt)
        {
            Status = ShipmentStatus.Planned,
            CarriedBy = carriedBy,
            Assignments = [new ShipmentAssignment(carriedBy, correlationId, plannedAt)],
        };
        shipment.RegisterDomainEvent(new ShipmentPlannedEvent(shipment));
        return shipment;
    }

    /// <summary>
    /// Reassigns a Planned Shipment to Own fleet or another Carrier, to fix the Planner's mistake before release: the
    /// reassignment joins the history and raises ShipmentReassigned. Returns false, changing nothing, for a Shipment that
    /// isn't Planned or is carried that way already.
    /// </summary>
    public bool Reassign(CarriedBy carriedBy, IdempotencyKey key, CorrelationId correlationId, DateTimeOffset reassignedAt)
    {
        ArgumentNullException.ThrowIfNull(carriedBy);
        ArgumentNullException.ThrowIfNull(key);
        if (Status != ShipmentStatus.Planned || carriedBy == CarriedBy)
        {
            return false;
        }

        var previous = Assignments[^1];
        var reassignment = new ShipmentAssignment(carriedBy, correlationId, reassignedAt, key);
        CarriedBy = carriedBy;
        Assignments = [.. Assignments, reassignment];
        RegisterDomainEvent(new ShipmentReassignedEvent(this, previous, reassignment));
        return true;
    }

    /// <summary>The reassignment made under the key, with the assignment it replaced; null when none was.</summary>
    public (ShipmentAssignment Previous, ShipmentAssignment Reassignment)? ReassignmentUnder(IdempotencyKey key)
    {
        for (var i = 1; i < Assignments.Count; i++)
        {
            if (Assignments[i].Key == key)
            {
                return (Assignments[i - 1], Assignments[i]);
            }
        }

        return null;
    }
}
