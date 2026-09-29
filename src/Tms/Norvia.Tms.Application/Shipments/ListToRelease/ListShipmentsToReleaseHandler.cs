using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

public sealed class ListShipmentsToReleaseHandler(
    IReadRepository<Shipment> repository, IReadRepository<CarrierBooking> carrierBookings, IShipperDirectory shippers, ICarrierDirectory carriers)
    : IQueryHandler<ListShipmentsToReleaseQuery, IReadOnlyList<ShipmentToReleaseSummary>>
{
    public async ValueTask<IReadOnlyList<ShipmentToReleaseSummary>> Handle(ListShipmentsToReleaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var shipments = await repository.ListAsync(new ShipmentsToReleaseSpec(), cancellationToken);
        var lastBookingByShipment = await LastBookingsByShipmentAsync(shipments, cancellationToken);

        return [.. shipments.Select(shipment => new ShipmentToReleaseSummary(
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            ShipperView.Of(shipment.ShipperId, shippers),
            CarriedByView.Of(shipment.CarriedBy, carriers),
            shipment.PlannedAt,
            lastBookingByShipment.TryGetValue(shipment.Id, out var last) ? LastCarrierBookingOutcome.Of(last, carriers) : null))];
    }

    // Every Shipment in this list is Planned, so any Carrier booking it has already ended without confirmation: the
    // spec's most-recently-requested-first order gives each Shipment's last one first within its group.
    private async Task<Dictionary<ShipmentId, CarrierBooking>> LastBookingsByShipmentAsync(IReadOnlyList<Shipment> shipments, CancellationToken cancellationToken)
    {
        var bookings = await carrierBookings.ListAsync(new CarrierBookingsForShipmentsSpec([.. shipments.Select(shipment => shipment.Id)]), cancellationToken);
        return bookings.GroupBy(booking => booking.ShipmentId).ToDictionary(group => group.Key, group => group.First());
    }
}
