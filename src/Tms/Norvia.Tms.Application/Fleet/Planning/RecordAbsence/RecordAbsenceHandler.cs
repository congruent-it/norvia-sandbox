using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RecordAbsence;

public sealed class RecordAbsenceHandler(
    IRepository<Absence> absences,
    IRepository<PlanningDay> days,
    IDriverDirectory drivers,
    PlanningHorizonRules horizon,
    TimeProvider time) : ICommandHandler<RecordAbsenceCommand, Result<RecordAbsenceResult>>
{
    public async ValueTask<Result<RecordAbsenceResult>> Handle(RecordAbsenceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (drivers.ListActive().All(driver => driver.Id != command.DriverId))
        {
            return PlanningRefused.Because<RecordAbsenceResult>(PlanningRefusal.DriverNotFound(command.DriverId.Value));
        }

        if (!AvailabilityPeriod.IsValid(command.From, command.To))
        {
            return PlanningRefused.Because<RecordAbsenceResult>(PlanningRefusal.PeriodInvalid(AvailabilityPeriod.MaxDays));
        }

        if (command.Remark?.Trim().Length > Absence.MaxRemarkLength)
        {
            return PlanningRefused.Because<RecordAbsenceResult>(PlanningRefusal.RemarkTooLong(Absence.MaxRemarkLength));
        }

        if (command.From < horizon.Today(time.GetUtcNow()))
        {
            return PlanningRefused.Because<RecordAbsenceResult>(PlanningRefusal.DayClosed(command.From));
        }

        var recorded = (await absences.ListAsync(new AbsencesOfDriverBetweenSpec(command.DriverId, command.From, command.To), cancellationToken))
            .ToDictionary(absence => absence.Date);
        var openDays = (await days.ListAsync(new OpenPlanningDaysFromSpec(command.From), cancellationToken)).ToDictionary(day => day.Date);

        foreach (var date in AvailabilityPeriod.Days(command.From, command.To))
        {
            if (recorded.TryGetValue(date, out var existing))
            {
                existing.Revise(command.Reason, command.Remark);
            }
            else
            {
                await absences.AddAsync(Absence.Record(command.DriverId, date, command.Reason, command.Remark), cancellationToken);
            }

            openDays.GetValueOrDefault(date)?.RecordAbsence(command.DriverId);
        }

        await absences.SaveChangesAsync(cancellationToken);
        return new RecordAbsenceResult(command.From, command.To, command.To.DayNumber - command.From.DayNumber + 1);
    }
}
