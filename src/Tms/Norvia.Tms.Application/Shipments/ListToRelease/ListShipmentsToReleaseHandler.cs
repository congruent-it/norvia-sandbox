using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

public sealed class ListShipmentsToReleaseHandler(
    IReadRepository<Shipment> repository,
    IReadRepository<CarrierBooking> carrierBookings,
    IReadRepository<TransportOrder> transportOrders,
    IShipperDirectory shippers,
    ICarrierDirectory carriers,
    ICarrierRateCardDirectory carrierRateCards,
    TimeProvider time)
    : IQueryHandler<ListShipmentsToReleaseQuery, IReadOnlyList<ShipmentToReleaseSummary>>
{
    public async ValueTask<IReadOnlyList<ShipmentToReleaseSummary>> Handle(ListShipmentsToReleaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var shipments = await repository.ListAsync(new ShipmentsToReleaseSpec(), cancellationToken);
        var lastBookingByShipment = await LastBookingsByShipmentAsync(shipments, cancellationToken);
        var ordersByNumber = await OrdersByNumberAsync(shipments, cancellationToken);
        var now = time.GetUtcNow();

        return [.. shipments.Select(shipment => new ShipmentToReleaseSummary(
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            ShipperView.Of(shipment.ShipperId, shippers),
            CarriedByView.Of(shipment.CarriedBy, carriers),
            shipment.PlannedAt,
            lastBookingByShipment.TryGetValue(shipment.Id, out var last) ? LastCarrierBookingOutcome.Of(last, carriers) : null,
            PricingFailureOf(shipment, ordersByNumber, now)))];
    }

    // Every Shipment in this list is Planned, so any Carrier booking it has already ended without confirmation: the
    // spec's most-recently-requested-first order gives each Shipment's last one first within its group.
    private async Task<Dictionary<ShipmentId, CarrierBooking>> LastBookingsByShipmentAsync(IReadOnlyList<Shipment> shipments, CancellationToken cancellationToken)
    {
        var bookings = await carrierBookings.ListAsync(new CarrierBookingsForShipmentsSpec([.. shipments.Select(shipment => shipment.Id)]), cancellationToken);
        return bookings.GroupBy(booking => booking.ShipmentId).ToDictionary(group => group.Key, group => group.First());
    }

    // The Transport Orders the Shipments carry out, which a Carrier Shipment is priced from.
    private async Task<Dictionary<TransportOrderNumber, TransportOrder>> OrdersByNumberAsync(IReadOnlyList<Shipment> shipments, CancellationToken cancellationToken)
    {
        var numbers = shipments.Where(shipment => !shipment.CarriedBy.IsOwnFleet).Select(shipment => shipment.TransportOrderNumber).Distinct().ToList();
        if (numbers.Count == 0)
        {
            return [];
        }

        var orders = await transportOrders.ListAsync(new TransportOrdersByNumbersSpec(numbers), cancellationToken);
        return orders.ToDictionary(order => order.Number!);
    }

    // The release prices a Carrier Shipment from its Carrier's Carrier Rate Card first and is refused when that fails, which is
    // when a Spot price is allowed (P9). Pricing is read-only, so the list can say so before the Planner tries a release.
    private PricingFailureView? PricingFailureOf(Shipment shipment, Dictionary<TransportOrderNumber, TransportOrder> orders, DateTimeOffset now)
    {
        if (shipment.CarriedBy.CarrierId is not { } carrierId
            || carriers.Find(carrierId) is not { } carrier
            || !orders.TryGetValue(shipment.TransportOrderNumber, out var order))
        {
            return null;
        }

        var (_, refusal) = AssignmentRules.PriceCarrierBooking(carrier, carrierRateCards.FindFor(carrierId), order.Details, now);
        return refusal is null ? null : new PricingFailureView(refusal.Code.ToString(), refusal.Message);
    }
}
