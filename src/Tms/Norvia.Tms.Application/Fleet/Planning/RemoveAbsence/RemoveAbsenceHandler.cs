using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RemoveAbsence;

public sealed class RemoveAbsenceHandler(
    IRepository<Absence> absences,
    IRepository<PlanningDay> days,
    EffectiveRosters rosters,
    PlanningHorizonRules horizon,
    TimeProvider time) : ICommandHandler<RemoveAbsenceCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveAbsenceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await absences.FirstOrDefaultAsync(new AbsenceByIdSpec(command.Id), cancellationToken) is not { } absence)
        {
            return PlanningRefused.Because(PlanningRefusal.AbsenceNotFound());
        }

        if (absence.Date < horizon.Today(time.GetUtcNow()))
        {
            return PlanningRefused.Because(PlanningRefusal.DayClosed(absence.Date));
        }

        await absences.DeleteAsync(absence, cancellationToken);
        var day = (await days.ListAsync(new OpenPlanningDaysFromSpec(absence.Date), cancellationToken)).FirstOrDefault(day => day.Date == absence.Date);
        day?.EndAbsence(absence.DriverId, await rosters.FindAsync(absence.DriverId, cancellationToken));

        await absences.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
