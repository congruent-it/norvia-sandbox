using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.OpenDays;

public sealed class OpenPlanningDaysHandler(
    IRepository<PlanningDay> days,
    IRosterDirectory rosters,
    IDriverDirectory drivers,
    IVehicleDirectory vehicles,
    PlanningHorizonRules horizon,
    TimeProvider time) : ICommandHandler<OpenPlanningDaysCommand, OpenPlanningDaysResult>
{
    public async ValueTask<OpenPlanningDaysResult> Handle(OpenPlanningDaysCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = horizon.Today(time.GetUtcNow());

        var closed = 0;
        foreach (var day in await days.ListAsync(new OpenPlanningDaysToCloseSpec(), cancellationToken))
        {
            day.CloseIfBefore(today);
            if (day.Status == PlanningDayStatus.Closed)
            {
                closed++;
            }
        }

        var existing = (await days.ListAsync(new PlanningDaysFromSpec(today), cancellationToken)).Select(day => day.Date).ToHashSet();
        var activeDrivers = drivers.ListActive().Select(driver => driver.Id).ToHashSet();
        var activeRosters = rosters.List().Where(roster => activeDrivers.Contains(roster.DriverId)).ToList();
        var availableVehicles = vehicles.ListActive().Select(vehicle => vehicle.Id).ToList();

        var missing = horizon.DaysFrom(today).Where(date => !existing.Contains(date)).ToList();
        foreach (var date in missing)
        {
            await days.AddAsync(PlanningDay.Open(date, activeRosters, availableVehicles), cancellationToken);
        }

        await days.SaveChangesAsync(cancellationToken);
        return new OpenPlanningDaysResult(today, missing, closed);
    }
}
