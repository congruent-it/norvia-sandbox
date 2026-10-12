using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.List;

public sealed class ListTripsHandler(
    IReadRepository<Trip> trips,
    TripShipments shipments,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    TripNeedsCheck needsCheck) : IQueryHandler<ListTripsQuery, IReadOnlyList<TripView>>
{
    public async ValueTask<IReadOnlyList<TripView>> Handle(ListTripsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stored = await trips.ListAsync(new TripsInOrderSpec(), cancellationToken);
        var onTrips = (await shipments.OfAsync([.. stored.SelectMany(trip => trip.ShipmentIds).Distinct()], cancellationToken)).ToDictionary(item => item.Shipment.Id);

        var needs = await needsCheck.OfAsync(stored, cancellationToken);
        var activeVehicles = vehicles.ListActive().ToDictionary(vehicle => vehicle.Id);
        var names = drivers.ListActive().ToDictionary(driver => driver.Id, driver => driver.Name);
        return
        [
            .. stored.Select(trip => TripViews.Of(
                trip,
                [.. trip.ShipmentIds.Where(onTrips.ContainsKey).Select(id => onTrips[id])],
                activeVehicles,
                names,
                needs[trip.Number.Value])),
        ];
    }
}
