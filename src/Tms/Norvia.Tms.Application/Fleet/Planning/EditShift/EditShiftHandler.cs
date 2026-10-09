using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.EditShift;

public sealed class EditShiftHandler(IRepository<PlanningDay> days, IDriverDirectory drivers, PlanningHorizonRules horizon, TimeProvider time)
    : ICommandHandler<EditShiftCommand, Result<EditShiftResult>>
{
    public async ValueTask<Result<EditShiftResult>> Handle(EditShiftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await days.FirstOrDefaultAsync(new PlanningDayByDateSpec(command.Date), cancellationToken) is not { } day)
        {
            return PlanningRefused.Because<EditShiftResult>(PlanningRefusal.DayNotFound(command.Date));
        }

        if (drivers.ListActive().All(driver => driver.Id != command.DriverId))
        {
            return PlanningRefused.Because<EditShiftResult>(PlanningRefusal.DriverNotFound(command.DriverId.Value));
        }

        day.CloseIfBefore(horizon.Today(time.GetUtcNow()));
        if (day.EditShift(command.DriverId, command.Start, command.End) is { } refusal)
        {
            return PlanningRefused.Because<EditShiftResult>(refusal);
        }

        await days.SaveChangesAsync(cancellationToken);
        return new EditShiftResult(day.Date, command.DriverId, command.Start, command.End);
    }
}
