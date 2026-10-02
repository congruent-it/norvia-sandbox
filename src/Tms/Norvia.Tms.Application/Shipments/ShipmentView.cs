using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments;

/// <param name="CorrelationId">The Message trail of the assignment that planned the Shipment.</param>
/// <param name="Assignments">The history of assignments, in order: the planning's first, then each reassignment's.</param>
/// <param name="CarrierBookings">
/// The Shipment's Carrier bookings, oldest first (P4): empty for an Own fleet Shipment, at most one open at a time.
/// </param>
public sealed record ShipmentView(
    string ShipmentNumber,
    ShipmentStatus Status,
    string TransportOrderNumber,
    ShipperView Shipper,
    CarriedByView CarriedBy,
    string CorrelationId,
    DateTimeOffset PlannedAt,
    IReadOnlyList<ShipmentAssignmentView> Assignments,
    IReadOnlyList<CarrierBookingView> CarrierBookings)
{
    public static ShipmentView Of(
        Shipment shipment, IReadOnlyList<CarrierBooking> carrierBookings, IShipperDirectory shippers, ICarrierDirectory carriers)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(carrierBookings);
        return new(
            shipment.Number.Value,
            shipment.Status,
            shipment.TransportOrderNumber.Value,
            ShipperView.Of(shipment.ShipperId, shippers),
            CarriedByView.Of(shipment.CarriedBy, carriers),
            shipment.CorrelationId.Value,
            shipment.PlannedAt,
            [.. shipment.Assignments.Select(assignment => ShipmentAssignmentView.Of(assignment, carriers))],
            [.. carrierBookings.Select(booking => CarrierBookingView.Of(booking, carriers))]);
    }
}
