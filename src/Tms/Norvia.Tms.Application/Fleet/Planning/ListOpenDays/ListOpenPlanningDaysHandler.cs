using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Fleet.Planning.ListOpenDays;

public sealed class ListOpenPlanningDaysHandler(
    IReadRepository<PlanningDay> days,
    DayLoads loads,
    IDriverDirectory drivers,
    IVehicleDirectory vehicles,
    IReadRepository<Trip> trips,
    TripNeedsCheck needsCheck)
    : IQueryHandler<ListOpenPlanningDaysQuery, IReadOnlyList<PlanningDayView>>
{
    public async ValueTask<IReadOnlyList<PlanningDayView>> Handle(ListOpenPlanningDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var activeDrivers = drivers.ListActive().ToDictionary(driver => driver.Id);
        var activeVehicles = vehicles.ListActive().ToDictionary(vehicle => vehicle.Id);
        var openDays = await days.ListAsync(new OpenPlanningDaysSpec(), cancellationToken);
        var demand = await loads.DemandByDayAsync(cancellationToken);
        var flagged = await FlaggedTripsByDayAsync([.. openDays.Select(day => day.Date)], cancellationToken);

        return
        [
            .. openDays.Select(day =>
            {
                var shifts = day.Shifts.Where(shift => activeDrivers.ContainsKey(shift.DriverId)).ToList();
                return new PlanningDayView(
                    day.Date,
                    day.AvailableVehicles.Count(activeVehicles.ContainsKey),
                    [
                        .. shifts
                            .Select(shift => new ShiftView(shift.DriverId.Value, activeDrivers[shift.DriverId].Name, shift.Start, shift.End, shift.EditedByHand))
                            .OrderBy(shift => shift.Start).ThenBy(shift => shift.DriverName, StringComparer.Ordinal),
                    ],
                    DayLoads.LoadOf(day, demand, activeDrivers, activeVehicles))
                {
                    FlaggedTrips = flagged.GetValueOrDefault(day.Date) ?? [],
                };
            }),
        ];
    }

    private async Task<Dictionary<DateOnly, IReadOnlyList<FlaggedTripView>>> FlaggedTripsByDayAsync(IReadOnlyCollection<DateOnly> openDays, CancellationToken cancellationToken)
    {
        var stored = await trips.ListAsync(new TripsOnDaysSpec(openDays), cancellationToken);
        var needs = await needsCheck.OfAsync(stored, cancellationToken);
        return stored
            .Where(trip => needs[trip.Number.Value].Any)
            .GroupBy(trip => trip.Day)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FlaggedTripView>)[.. group.Select(trip => new FlaggedTripView(trip.Number.Value, needs[trip.Number.Value].Driver, needs[trip.Number.Value].Vehicle))]);
    }
}
