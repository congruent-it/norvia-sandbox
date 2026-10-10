using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Choices;

public sealed class ListTripChoicesHandler(
    IReadRepository<PlanningDay> days,
    IReadRepository<Trip> trips,
    IVehicleDirectory vehicles,
    IDriverDirectory drivers) : IQueryHandler<ListTripChoicesQuery, TripChoices>
{
    public async ValueTask<TripChoices> Handle(ListTripChoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await days.FirstOrDefaultAsync(new PlanningDayByDateSpec(query.Day), cancellationToken) is not { Status: PlanningDayStatus.Open } day)
        {
            return new TripChoices([], []);
        }

        var others = (await trips.ListAsync(new TripsOnDaySpec(query.Day), cancellationToken))
            .Where(trip => trip.Number.Value != query.ForTrip)
            .ToList();
        return new TripChoices(
            [
                .. vehicles.ListActive()
                    .Where(vehicle => day.IsAvailable(vehicle.Id) && others.All(trip => trip.VehicleId != vehicle.Id))
                    .Select(vehicle => new VehicleChoice(vehicle.Id.Value, vehicle.Registration)),
            ],
            [
                .. drivers.ListActive()
                    .Where(driver => day.HasShift(driver.Id) && others.All(trip => trip.DriverId != driver.Id))
                    .Select(driver => new DriverChoice(driver.Id.Value, driver.Name)),
            ]);
    }
}
