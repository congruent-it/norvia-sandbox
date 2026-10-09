using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.ListOpenDays;

public sealed class ListOpenPlanningDaysHandler(
    IReadRepository<PlanningDay> days,
    DayLoads loads,
    IDriverDirectory drivers,
    IVehicleDirectory vehicles)
    : IQueryHandler<ListOpenPlanningDaysQuery, IReadOnlyList<PlanningDayView>>
{
    public async ValueTask<IReadOnlyList<PlanningDayView>> Handle(ListOpenPlanningDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var activeDrivers = drivers.ListActive().ToDictionary(driver => driver.Id);
        var activeVehicles = vehicles.ListActive().ToDictionary(vehicle => vehicle.Id);
        var openDays = await days.ListAsync(new OpenPlanningDaysSpec(), cancellationToken);
        var demand = await loads.DemandByDayAsync(cancellationToken);

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
                    DayLoads.LoadOf(day, demand, activeDrivers, activeVehicles));
            }),
        ];
    }
}
