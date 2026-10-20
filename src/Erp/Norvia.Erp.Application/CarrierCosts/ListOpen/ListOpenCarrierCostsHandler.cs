using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierCosts.ListOpen;

public sealed class ListOpenCarrierCostsHandler(IReadRepository<CarrierCost> costs, IReadRepository<Carrier> carriers)
    : IQueryHandler<ListOpenCarrierCostsQuery, IReadOnlyList<OpenCarrierCostsOfCarrierView>>
{
    public async ValueTask<IReadOnlyList<OpenCarrierCostsOfCarrierView>> Handle(ListOpenCarrierCostsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var open = await costs.ListAsync(new OpenCarrierCostsSpec(), cancellationToken);
        var names = (await carriers.ListAsync(new CarriersSpec(), cancellationToken)).ToDictionary(carrier => carrier.Id, carrier => carrier.Name);

        return
        [
            .. open
                .GroupBy(cost => cost.CarrierId)
                .Select(group => new OpenCarrierCostsOfCarrierView(
                    group.Key.Value,
                    names.GetValueOrDefault(group.Key, group.Key.Value),
                    group.Sum(cost => cost.Total),
                    [.. group.Select(CarrierCostViews.Of)]))
                .OrderBy(view => view.CarrierName, StringComparer.CurrentCulture),
        ];
    }
}
