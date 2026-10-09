using Mediator;

namespace Norvia.Tms.Application.Fleet.Planning.ListRosters;

public sealed class ListRostersHandler(EffectiveRosters rosters, IDriverDirectory drivers) : IQueryHandler<ListRostersQuery, IReadOnlyList<RosterView>>
{
    public async ValueTask<IReadOnlyList<RosterView>> Handle(ListRostersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var byDriver = (await rosters.ListAsync(cancellationToken)).ToDictionary(roster => roster.DriverId);

        return
        [
            .. drivers.ListActive().Select(driver => new RosterView(
                driver.Id.Value,
                driver.Name,
                byDriver.TryGetValue(driver.Id, out var roster)
                    ? [.. roster.Weekdays.OrderBy(entry => ((int)entry.Key + 6) % 7).Select(entry => new RosterWeekdayView(entry.Key, entry.Value.Start, entry.Value.End))]
                    : [])),
        ];
    }
}
