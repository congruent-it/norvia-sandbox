using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// A request to one Carrier to carry one Shipment, together with that Carrier's answer. A Shipment can have several
/// Carrier bookings over its life, one after each that ends without being carried out, but only one open at a time
/// (P4). The TMS owns it. This build only requests it, at release; the Carrier's answer, sent/not-delivered reporting
/// and withdrawal are later tickets.
/// </summary>
public sealed class CarrierBooking : EntityBase<CarrierBookingId>, IAggregateRoot
{
    private CarrierBooking(
        CarrierBookingId id,
        CarrierBookingNumber number,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        CarrierId carrierId,
        CorrelationId correlationId,
        DateTimeOffset requestedAt)
    {
        Id = id;
        Number = number;
        ShipmentId = shipmentId;
        ShipmentNumber = shipmentNumber;
        CarrierId = carrierId;
        CorrelationId = correlationId;
        RequestedAt = requestedAt;
        Status = CarrierBookingStatus.Requested;
    }

    public CarrierBookingNumber Number { get; private set; }

    public ShipmentId ShipmentId { get; private set; }

    public ShipmentNumber ShipmentNumber { get; private set; }

    public CarrierId CarrierId { get; private set; }

    public CarrierBookingStatus Status { get; private set; }

    /// <summary>The Message trail of the release that created the booking.</summary>
    public CorrelationId CorrelationId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>
    /// Requests a Carrier booking for a Shipment's release, Requested. Raises no domain event of its own: the release
    /// that creates it announces both in <c>ShipmentReleased</c>.
    /// </summary>
    public static CarrierBooking Request(
        CarrierBookingNumber number,
        ShipmentId shipmentId,
        ShipmentNumber shipmentNumber,
        CarrierId carrierId,
        CorrelationId correlationId,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(carrierId);
        return new CarrierBooking(CarrierBookingId.New(), number, shipmentId, shipmentNumber, carrierId, correlationId, requestedAt);
    }
}
