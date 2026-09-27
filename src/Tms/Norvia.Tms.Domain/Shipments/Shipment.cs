using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// The physical movement that carries out one Transport Order. Each Transport Order has exactly one Shipment, and a
/// Shipment never carries more than one Transport Order (ADR 0007). The TMS owns it. It is made only by planning its
/// Transport Order (<see cref="TransportOrder.Plan"/>), and never deleted.
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

    public CarriedBy CarriedBy { get; private set; } = CarriedBy.OwnFleet;

    /// <summary>
    /// The Message trail of the Planner's assignment, which starts in the TMS UI and so gets a trail of its own, not the
    /// intake's.
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
        };
        shipment.RegisterDomainEvent(new ShipmentPlannedEvent(shipment));
        return shipment;
    }
}
