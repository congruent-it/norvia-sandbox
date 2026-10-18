using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.BillableLines.ListWaiting;

public sealed class ListWaitingBillableLinesHandler(IReadRepository<BillableLine> lines, IReadRepository<Shipper> shippers)
    : IQueryHandler<ListWaitingBillableLinesQuery, IReadOnlyList<WaitingShipperView>>
{
    public async ValueTask<IReadOnlyList<WaitingShipperView>> Handle(ListWaitingBillableLinesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var waiting = await lines.ListAsync(new WaitingBillableLinesSpec(), cancellationToken);
        var names = (await shippers.ListAsync(new ShippersSpec(), cancellationToken)).ToDictionary(shipper => shipper.Id, shipper => shipper.Name);

        return
        [
            .. waiting
                .GroupBy(line => line.ShipperId)
                .Select(group => new WaitingShipperView(
                    group.Key.Value, names.GetValueOrDefault(group.Key, group.Key.Value), group.Count(), group.Sum(line => line.Total)))
                .OrderBy(view => view.ShipperName, StringComparer.CurrentCulture),
        ];
    }
}
