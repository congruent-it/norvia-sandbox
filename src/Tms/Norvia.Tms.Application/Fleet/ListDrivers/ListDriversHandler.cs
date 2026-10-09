using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.ListDrivers;

public sealed class ListDriversHandler(
    IDriverDirectory drivers,
    IPlannedDrivingHours plannedDriving,
    DrivingHourLimits limits,
    PlanningHorizonRules horizon,
    TimeProvider time) : IQueryHandler<ListDriversQuery, IReadOnlyList<DriverView>>
{
    public async ValueTask<IReadOnlyList<DriverView>> Handle(ListDriversQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = horizon.Today(time.GetUtcNow());
        var (first, last) = DrivingHourBudget.Window(today);
        var planned = await plannedDriving.ListAsync(first, last, cancellationToken);
        var byDriver = planned.ToLookup(driving => driving.DriverId);

        return
        [
            .. drivers.ListActive().Select(driver => new DriverView(
                driver.Id.Value,
                driver.Name,
                [.. driver.Licences.Order()],
                [.. Enum.GetValues<VehicleType>().Where(driver.CanDrive)],
                DrivingHourBudget.Left(byDriver[driver.Id], today, limits))),
        ];
    }
}
