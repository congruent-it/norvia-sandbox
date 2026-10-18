using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.BillableLines.ListWaitingOfShipper;

public sealed class ListWaitingBillableLinesOfShipperHandler(IReadRepository<BillableLine> lines, IReadRepository<Shipper> shippers)
    : IQueryHandler<ListWaitingBillableLinesOfShipperQuery, WaitingShipperLinesView?>
{
    public async ValueTask<WaitingShipperLinesView?> Handle(ListWaitingBillableLinesOfShipperQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var id = new ShipperId(query.ShipperId);
        if (await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(id), cancellationToken) is not { } shipper)
        {
            return null;
        }

        var waiting = await lines.ListAsync(new WaitingBillableLinesOfShipperSpec(id), cancellationToken);
        return new WaitingShipperLinesView(shipper.Id.Value, shipper.Name, waiting.Sum(line => line.Total), [.. waiting.Select(BillableLineViews.Of)]);
    }
}
