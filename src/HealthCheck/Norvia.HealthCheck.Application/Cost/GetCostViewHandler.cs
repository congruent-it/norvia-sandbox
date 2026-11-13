using Ardalis.Result;
using Mediator;

namespace Norvia.HealthCheck.Application.Cost;

/// <summary>
/// Reads two periods of <see cref="PeriodDays"/> whole days ending yesterday and compares their daily averages, so a period the reader holds
/// fewer days of is still comparable. A provisional day is shown but left out of the averages: its number may still change, and the page never guesses it.
/// </summary>
public sealed class GetCostViewHandler(ICostReader reader, TimeProvider time) : IQueryHandler<GetCostViewQuery, Result<CostView>>
{
    public const int PeriodDays = 7;

    public async ValueTask<Result<CostView>> Handle(GetCostViewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var yesterday = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-1);
        var currentFrom = yesterday.AddDays(1 - PeriodDays);
        var previousFrom = currentFrom.AddDays(-PeriodDays);

        var days = (await reader.ReadAsync(previousFrom, yesterday, cancellationToken)).OrderBy(day => day.Date).ToList();
        var settled = days.Where(day => !day.Provisional).ToList();
        var previous = settled.Where(day => day.Date < currentFrom).ToList();
        var current = settled.Where(day => day.Date >= currentFrom).ToList();

        PeriodChange Change(string name, Func<CostDay, decimal> figure, ChangeKind kind) =>
            new(name, Average(previous, figure), Average(current, figure), kind);

        var view = new CostView(PeriodDays, days, [], Change("Total", day => day.TotalEur, ChangeKind.Total), Change("Transport Orders", day => day.TransportOrders, ChangeKind.Volume));
        var resources = view.ResourceNames
            .Select(resource => Change(resource, day => day.SpendOn(resource), ChangeKind.Resource))
            .OrderByDescending(change => change.Relative ?? decimal.MinValue)
            .ThenBy(change => change.Name, StringComparer.Ordinal)
            .ToList();

        return view with { Resources = resources };
    }

    private static decimal? Average(List<CostDay> days, Func<CostDay, decimal> figure) => days.Count == 0 ? null : days.Average(figure);
}
