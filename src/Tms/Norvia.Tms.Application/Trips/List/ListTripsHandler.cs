using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.List;

public sealed class ListTripsHandler(
    IReadRepository<Trip> trips,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers,
    PlanningHorizonRules horizon,
    TimeProvider time) : IQueryHandler<ListTripsQuery, IReadOnlyList<TripView>>
{
    public async ValueTask<IReadOnlyList<TripView>> Handle(ListTripsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stored = await trips.ListAsync(new TripsFromSpec(horizon.Today(time.GetUtcNow())), cancellationToken);

        // A Draft may hold a Vehicle or Driver that has since gone inactive: it is still named, by its ID.
        var registrations = vehicles.ListActive().ToDictionary(vehicle => vehicle.Id, vehicle => vehicle.Registration);
        var names = drivers.ListActive().ToDictionary(driver => driver.Id, driver => driver.Name);
        return
        [
            .. stored.Select(trip => new TripView(
                trip.Number.Value,
                trip.Day,
                trip.Status,
                trip.VehicleId?.Value,
                trip.VehicleId is { } vehicle ? registrations.GetValueOrDefault(vehicle, vehicle.Value) : null,
                trip.DriverId?.Value,
                trip.DriverId is { } driver ? names.GetValueOrDefault(driver, driver.Value) : null)),
        ];
    }
}
