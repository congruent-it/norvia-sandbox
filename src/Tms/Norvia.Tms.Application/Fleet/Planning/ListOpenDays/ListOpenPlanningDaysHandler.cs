using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.ListOpenDays;

public sealed class ListOpenPlanningDaysHandler(IReadRepository<PlanningDay> days, IDriverDirectory drivers)
    : IQueryHandler<ListOpenPlanningDaysQuery, IReadOnlyList<PlanningDayView>>
{
    public async ValueTask<IReadOnlyList<PlanningDayView>> Handle(ListOpenPlanningDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var names = drivers.ListActive().ToDictionary(driver => driver.Id, driver => driver.Name);

        return
        [
            .. (await days.ListAsync(new OpenPlanningDaysSpec(readOnly: true), cancellationToken)).Select(day => new PlanningDayView(
                day.Date,
                day.AvailableVehicles.Count,
                [
                    .. day.Shifts
                        .Where(shift => names.ContainsKey(shift.DriverId))
                        .Select(shift => new ShiftView(shift.DriverId.Value, names[shift.DriverId], shift.Start, shift.End, shift.EditedByHand))
                        .OrderBy(shift => shift.Start).ThenBy(shift => shift.DriverName, StringComparer.Ordinal),
                ])),
        ];
    }
}
