using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.MasterData.Carriers.List;

public sealed class ListCarriersHandler(IReadRepository<Carrier> carriers) : IQueryHandler<ListCarriersQuery, IReadOnlyList<CarrierView>>
{
    public async ValueTask<IReadOnlyList<CarrierView>> Handle(ListCarriersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await carriers.ListAsync(new CarriersSpec(), cancellationToken);
        return [.. all.Select(carrier => new CarrierView(carrier.Id.Value, carrier.Name, carrier.Active))];
    }
}
