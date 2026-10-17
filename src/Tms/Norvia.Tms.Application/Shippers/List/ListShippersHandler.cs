using Mediator;
using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Application.Shippers.List;

public sealed class ListShippersHandler(IShipperDirectory shippers) : IQueryHandler<ListShippersQuery, IReadOnlyList<ShipperView>>
{
    public ValueTask<IReadOnlyList<ShipperView>> Handle(ListShippersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ValueTask.FromResult<IReadOnlyList<ShipperView>>(
            [.. shippers.List().OrderBy(shipper => shipper.Name, StringComparer.Ordinal).Select(shipper => new ShipperView(shipper.Id.Value, shipper.Name))]);
    }
}
