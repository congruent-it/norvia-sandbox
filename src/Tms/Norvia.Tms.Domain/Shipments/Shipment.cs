using Ardalis.SharedKernel;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// The physical movement that carries out one Transport Order. Each Transport Order has exactly one Shipment, and a
/// Shipment never carries more than one Transport Order (ADR 0007). The TMS owns it. It is made only by planning its
/// Transport Order (<see cref="TransportOrder.Plan"/>), and never deleted. While it is Planned, the Planner can
/// reassign it; it keeps its Shipment number and records each assignment in its history. Releasing it hands it over,
/// after which P3 can no longer change it.
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

    /// <summary>The Planner's release that handed the Shipment over; null while it is Planned and never released.</summary>
    public ShipmentRelease? LastRelease { get; private set; }

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

    /// <summary>
    /// Releases a Planned Shipment, handing it over to be carried out with its Transport Order's details: it becomes
    /// Released, P3 can no longer change it, and ShipmentReleased is raised. Returns the release, or null, changing nothing,
    /// for a Shipment that isn't Planned.
    /// </summary>
    /// <param name="order">The Transport Order the Shipment carries out.</param>
    /// <param name="carrierBookingNumber">
    /// The Carrier booking number the release creates (P4); required for a Carrier Shipment, and none for an Own fleet one.
    /// </param>
    public ShipmentRelease? Release(
        TransportOrder order, IdempotencyKey key, CorrelationId correlationId, DateTimeOffset releasedAt, CarrierBookingNumber? carrierBookingNumber = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(key);
        if (order.Id != TransportOrderId)
        {
            throw new ArgumentException($"Shipment {Number} carries out Transport Order {TransportOrderNumber}, not {order.Number}.", nameof(order));
        }

        if (Status != ShipmentStatus.Planned)
        {
            return null;
        }

        if (CarriedBy.IsOwnFleet == (carrierBookingNumber is not null))
        {
            throw new ArgumentException(
                $"Shipment {Number} is carried by {CarriedBy}, so its release needs "
                + (CarriedBy.IsOwnFleet ? "no Carrier booking number." : "a Carrier booking number."),
                nameof(carrierBookingNumber));
        }

        var release = new ShipmentRelease(correlationId, releasedAt, key, carrierBookingNumber);
        Status = ShipmentStatus.Released;
        LastRelease = release;
        RegisterDomainEvent(new ShipmentReleasedEvent(this, order, release));
        return release;
    }

    /// <summary>
    /// Confirms a Released Shipment as Booked, once its Carrier booking is Confirmed (P4). Raises no domain event of its
    /// own: the Carrier booking's own event already says so. Returns false, changing nothing, for a Shipment that isn't
    /// Released.
    /// </summary>
    public bool Book()
    {
        if (Status != ShipmentStatus.Released)
        {
            return false;
        }

        Status = ShipmentStatus.Booked;
        return true;
    }

    /// <summary>
    /// Sends a Released Shipment back to Planned, because its Carrier booking ended without confirmation (P4): rejected,
    /// not delivered or withdrawn. Raises no domain event of its own: the Carrier booking's own event already says so.
    /// Returns false, changing nothing, for a Shipment that isn't Released.
    /// </summary>
    public bool ReturnToPlanned()
    {
        if (Status != ShipmentStatus.Released)
        {
            return false;
        }

        Status = ShipmentStatus.Planned;
        return true;
    }

    /// <summary>
    /// Takes in a Tracking event reported for the Shipment (P5): the event is made, to be kept as a log on the Shipment,
    /// and the status moves forward where the event warrants it. The status only moves forward: an event moves a Booked or
    /// later Shipment to its type's status when that is further along than the current one, and any step can be skipped.
    /// It raises the event for the status landed on only. Any other event, such as a late one or one after Delivered, is
    /// only logged, changing nothing and raising nothing.
    /// </summary>
    public TrackingEvent Record(TrackingReport report, CorrelationId correlationId, DateTimeOffset receivedAt)
    {
        var trackingEvent = TrackingEvent.Record(this, report, correlationId, receivedAt);
        var target = StatusOf(report.Type);
        if (Status >= ShipmentStatus.Booked && target > Status)
        {
            Status = target;
            trackingEvent.MarkMovedShipment();
            RegisterDomainEvent(target switch
            {
                ShipmentStatus.Collected => new ShipmentCollectedEvent(this, trackingEvent),
                ShipmentStatus.InTransit => new ShipmentInTransitEvent(this, trackingEvent),
                _ => new ShipmentDeliveredEvent(this, trackingEvent),
            });
        }

        return trackingEvent;
    }

    private static ShipmentStatus StatusOf(TrackingEventType type) => type switch
    {
        TrackingEventType.Collected => ShipmentStatus.Collected,
        TrackingEventType.Departed or TrackingEventType.Arrived or TrackingEventType.OutForDelivery => ShipmentStatus.InTransit,
        TrackingEventType.Delivered => ShipmentStatus.Delivered,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The TMS doesn't know what status this Tracking event type moves a Shipment to."),
    };

    /// <summary>The release made under the key; null when none was.</summary>
    public ShipmentRelease? ReleaseUnder(IdempotencyKey key) => LastRelease is { } release && release.Key == key ? release : null;

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
