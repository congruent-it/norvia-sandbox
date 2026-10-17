using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.TransportOrders.GetDetail;

public sealed class GetTransportOrderDetailHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<CarrierBooking> carrierBookings,
    IReadRepository<Trip> trips,
    TripShipments tripShipments,
    TripOutcomes tripOutcomes,
    IShipperDirectory shippers,
    ICarrierDirectory carriers) : IQueryHandler<GetTransportOrderDetailQuery, Result<TransportOrderDetailView>>
{
    public async ValueTask<Result<TransportOrderDetailView>> Handle(GetTransportOrderDetailQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { } order)
        {
            return Result.NotFound();
        }

        var view = TransportOrderView.Of(order, shippers);
        var shipment = order.ShipmentNumber is null
            ? null
            : await shipments.FirstOrDefaultAsync(new ShipmentByNumberSpec(order.ShipmentNumber), cancellationToken);
        if (shipment is null)
        {
            return new TransportOrderDetailView(view, null, null);
        }

        var bookings = await carrierBookings.ListAsync(new CarrierBookingsByShipmentSpec(shipment.Id), cancellationToken);
        // Oldest first, so the last is the latest; an open one, still waiting for the Carrier, comes before it.
        var booking = bookings.FirstOrDefault(candidate => candidate.Status == CarrierBookingStatus.Requested) ?? bookings.LastOrDefault();
        var shipmentView = new TransportOrderShipmentView(
            shipment.Number.Value, shipment.Status, CarriedByView.Of(shipment.CarriedBy, carriers), booking is null ? null : CarrierBookingView.Of(booking, carriers));

        // A Shipment is on at most one Trip.
        var trip = await trips.FirstOrDefaultAsync(new TripsOfShipmentsSpec([shipment.Id]), cancellationToken);
        if (trip is null)
        {
            return new TransportOrderDetailView(view, shipmentView, null);
        }

        var onTrip = await tripShipments.OfAsync([shipment.Id], cancellationToken);
        var outcomes = (await tripOutcomes.OfAsync([trip.Number], cancellationToken)).GetValueOrDefault(trip.Number);
        var stops = trip.Stops
            .Where(stop => stop.ShipmentId == shipment.Id)
            .Select(stop => StopText.ViewOf(stop, onTrip.Single(), outcomes?.Of(stop.ShipmentId, stop.Kind) ?? StopOutcome.Open))
            .ToList();
        return new TransportOrderDetailView(view, shipmentView, new TransportOrderTripView(trip.Number.Value, trip.Day, trip.Status, stops));
    }
}
