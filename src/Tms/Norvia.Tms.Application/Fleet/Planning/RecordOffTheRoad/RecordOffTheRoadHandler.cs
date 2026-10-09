using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;

public sealed class RecordOffTheRoadHandler(
    IRepository<OffTheRoadDay> offTheRoadDays,
    IRepository<PlanningDay> days,
    IVehicleDirectory vehicles,
    PlanningHorizonRules horizon,
    TimeProvider time) : ICommandHandler<RecordOffTheRoadCommand, Result<RecordOffTheRoadResult>>
{
    public async ValueTask<Result<RecordOffTheRoadResult>> Handle(RecordOffTheRoadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (vehicles.ListActive().All(vehicle => vehicle.Id != command.VehicleId))
        {
            return PlanningRefused.Because<RecordOffTheRoadResult>(PlanningRefusal.VehicleNotFound(command.VehicleId.Value));
        }

        if (!AvailabilityPeriod.IsValid(command.From, command.To))
        {
            return PlanningRefused.Because<RecordOffTheRoadResult>(PlanningRefusal.PeriodInvalid(AvailabilityPeriod.MaxDays));
        }

        if (command.Remark?.Trim().Length > Absence.MaxRemarkLength)
        {
            return PlanningRefused.Because<RecordOffTheRoadResult>(PlanningRefusal.RemarkTooLong(Absence.MaxRemarkLength));
        }

        if (command.From < horizon.Today(time.GetUtcNow()))
        {
            return PlanningRefused.Because<RecordOffTheRoadResult>(PlanningRefusal.DayClosed(command.From));
        }

        var recorded = (await offTheRoadDays.ListAsync(new OffTheRoadDaysOfVehicleBetweenSpec(command.VehicleId, command.From, command.To), cancellationToken))
            .ToDictionary(day => day.Date);
        var openDays = (await days.ListAsync(new OpenPlanningDaysFromSpec(command.From), cancellationToken)).ToDictionary(day => day.Date);

        foreach (var date in AvailabilityPeriod.Days(command.From, command.To))
        {
            if (recorded.TryGetValue(date, out var existing))
            {
                existing.Revise(command.Reason, command.Remark);
            }
            else
            {
                await offTheRoadDays.AddAsync(OffTheRoadDay.Record(command.VehicleId, date, command.Reason, command.Remark), cancellationToken);
            }

            openDays.GetValueOrDefault(date)?.TakeOffTheRoad(command.VehicleId);
        }

        await offTheRoadDays.SaveChangesAsync(cancellationToken);
        return new RecordOffTheRoadResult(command.From, command.To, command.To.DayNumber - command.From.DayNumber + 1);
    }
}
