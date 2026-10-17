using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.TransportOrders.GetDetail;

/// <param name="Shipment">The Transport Order's one Shipment (ADR 0007); null until it is Planned.</param>
/// <param name="Trip">The Trip the Shipment is on; null when it is on none.</param>
public sealed record TransportOrderDetailView(TransportOrderView TransportOrder, TransportOrderShipmentView? Shipment, TransportOrderTripView? Trip);

/// <param name="CarriedBy">Who carries the Shipment now: a reassignment keeps the Shipment and changes this.</param>
/// <param name="CarrierBooking">The open Carrier booking, otherwise the latest; null for an Own fleet Shipment or one not yet released.</param>
public sealed record TransportOrderShipmentView(string ShipmentNumber, ShipmentStatus Status, CarriedByView CarriedBy, CarrierBookingView? CarrierBooking);

/// <param name="Stops">The Stops of this Transport Order's Shipment on the Trip, with the outcome the Driver reported for each.</param>
public sealed record TransportOrderTripView(string TripNumber, DateOnly Day, TripStatus Status, IReadOnlyList<TripStopView> Stops);
