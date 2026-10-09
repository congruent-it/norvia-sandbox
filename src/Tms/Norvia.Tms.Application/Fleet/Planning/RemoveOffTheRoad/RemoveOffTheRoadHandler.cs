using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RemoveOffTheRoad;

public sealed class RemoveOffTheRoadHandler(
    IRepository<OffTheRoadDay> offTheRoadDays,
    IRepository<PlanningDay> days,
    PlanningHorizonRules horizon,
    TimeProvider time) : ICommandHandler<RemoveOffTheRoadCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveOffTheRoadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await offTheRoadDays.FirstOrDefaultAsync(new OffTheRoadDayByIdSpec(command.Id), cancellationToken) is not { } offTheRoad)
        {
            return PlanningRefused.Because(PlanningRefusal.OffTheRoadDayNotFound());
        }

        if (offTheRoad.Date < horizon.Today(time.GetUtcNow()))
        {
            return PlanningRefused.Because(PlanningRefusal.DayClosed(offTheRoad.Date));
        }

        await offTheRoadDays.DeleteAsync(offTheRoad, cancellationToken);
        var day = (await days.ListAsync(new OpenPlanningDaysFromSpec(offTheRoad.Date), cancellationToken)).FirstOrDefault(day => day.Date == offTheRoad.Date);
        day?.PutBackOnTheRoad(offTheRoad.VehicleId);

        await offTheRoadDays.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
