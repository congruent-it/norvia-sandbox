using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Shipments.ListToPutOnTrips;

public sealed class ListShipmentsToPutOnTripsHandler(
    IReadRepository<Shipment> shipments,
    IReadRepository<Trip> trips,
    TripShipments tripShipments,
    IShipperDirectory shippers) : IQueryHandler<ListShipmentsToPutOnTripsQuery, IReadOnlyList<ShipmentToPutOnTripSummary>>
{
    public async ValueTask<IReadOnlyList<ShipmentToPutOnTripSummary>> Handle(ListShipmentsToPutOnTripsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var released = await shipments.ListAsync(new ShipmentsToPutOnTripsSpec(), cancellationToken);
        if (released.Count == 0)
        {
            return [];
        }

        var onTrips = (await trips.ListAsync(new TripsInOrderSpec(), cancellationToken)).SelectMany(trip => trip.ShipmentIds).ToHashSet();
        var free = await tripShipments.OfAsync([.. released.Where(shipment => !onTrips.Contains(shipment.Id)).Select(shipment => shipment.Id)], cancellationToken);
        return
        [
            .. free
                .Where(item => item.Shipment.AwaitingRedelivery || query.Day is not { } day || item.CollectionDay == day)
                .OrderBy(item => item.Details.Collection?.Window?.Earliest ?? DateTimeOffset.MaxValue)
                .ThenBy(item => item.Shipment.PlannedAt)
                .Select(Summarize),
        ];
    }

    private ShipmentToPutOnTripSummary Summarize(ShipmentOnTrip item)
    {
        var details = item.Details;
        var load = item.Load;
        return new ShipmentToPutOnTripSummary(
            item.Shipment.Number.Value,
            item.Shipment.TransportOrderNumber.Value,
            ShipperView.Of(item.Shipment.ShipperId, shippers),
            item.CollectionDay,
            StopText.PlaceOf(details.Collection?.Address),
            details.Collection?.Window?.Earliest,
            details.Collection?.Window?.Latest,
            StopText.PlaceOf(details.Delivery?.Address),
            item.DeliveryWindow?.Earliest,
            item.DeliveryWindow?.Latest,
            load.WeightKg,
            load.PalletPlaces,
            item.Shipment.AwaitingRedelivery);
    }
}
