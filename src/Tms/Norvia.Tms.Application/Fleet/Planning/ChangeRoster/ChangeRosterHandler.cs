using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.ChangeRoster;

public sealed class ChangeRosterHandler(
    IRepository<Roster> rosters,
    IRepository<PlanningDay> days,
    IReadRepository<Absence> absences,
    IDriverDirectory drivers,
    PlanningHorizonRules horizon,
    TimeProvider time) : ICommandHandler<ChangeRosterCommand, Result<ChangeRosterResult>>
{
    public async ValueTask<Result<ChangeRosterResult>> Handle(ChangeRosterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (drivers.ListActive().All(driver => driver.Id != command.DriverId))
        {
            return PlanningRefused.Because<ChangeRosterResult>(PlanningRefusal.DriverNotFound(command.DriverId.Value));
        }

        var roster = new Roster(command.DriverId, command.Weekdays);
        if (await rosters.FirstOrDefaultAsync(new RosterByDriverSpec(command.DriverId), cancellationToken) is null)
        {
            await rosters.AddAsync(roster, cancellationToken);
        }
        else
        {
            await rosters.UpdateAsync(roster, cancellationToken);
        }

        var today = horizon.Today(time.GetUtcNow());
        var absentOn = (await absences.ListAsync(new AbsencesFromSpec(today), cancellationToken))
            .Where(absence => absence.DriverId == command.DriverId)
            .Select(absence => absence.Date)
            .ToHashSet();

        var rewritten = 0;
        foreach (var day in await days.ListAsync(new OpenPlanningDaysFromSpec(today), cancellationToken))
        {
            if (day.ApplyRoster(roster, absentOn.Contains(day.Date)))
            {
                rewritten++;
            }
        }

        await rosters.SaveChangesAsync(cancellationToken);
        return new ChangeRosterResult(rewritten);
    }
}
