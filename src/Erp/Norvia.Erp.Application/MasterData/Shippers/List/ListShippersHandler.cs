using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.MasterData.Shippers.List;

public sealed class ListShippersHandler(IReadRepository<Shipper> shippers) : IQueryHandler<ListShippersQuery, IReadOnlyList<ShipperView>>
{
    public async ValueTask<IReadOnlyList<ShipperView>> Handle(ListShippersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await shippers.ListAsync(new ShippersSpec(), cancellationToken);
        return [.. all.Select(shipper => new ShipperView(shipper.Id.Value, shipper.Name, shipper.Active, shipper.Revision))];
    }
}
