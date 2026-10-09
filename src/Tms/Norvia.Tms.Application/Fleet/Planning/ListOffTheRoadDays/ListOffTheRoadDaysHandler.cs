using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.ListOffTheRoadDays;

public sealed class ListOffTheRoadDaysHandler(IReadRepository<OffTheRoadDay> offTheRoadDays, IVehicleDirectory vehicles, PlanningHorizonRules horizon, TimeProvider time)
    : IQueryHandler<ListOffTheRoadDaysQuery, IReadOnlyList<OffTheRoadDayView>>
{
    public async ValueTask<IReadOnlyList<OffTheRoadDayView>> Handle(ListOffTheRoadDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var registrations = vehicles.ListActive().ToDictionary(vehicle => vehicle.Id, vehicle => vehicle.Registration);

        return
        [
            .. (await offTheRoadDays.ListAsync(new OffTheRoadDaysFromSpec(horizon.Today(time.GetUtcNow())), cancellationToken))
                .Where(day => registrations.ContainsKey(day.VehicleId))
                .Select(day => new OffTheRoadDayView(day.Id.Value, day.VehicleId.Value, registrations[day.VehicleId], day.Date, day.Reason, day.Remark))
                .OrderBy(day => day.Date).ThenBy(day => day.Registration, StringComparer.Ordinal),
        ];
    }
}
