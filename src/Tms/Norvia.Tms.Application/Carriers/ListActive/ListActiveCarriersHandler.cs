using Mediator;
using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Application.Carriers.ListActive;

public sealed class ListActiveCarriersHandler(ICarrierDirectory carriers)
    : IQueryHandler<ListActiveCarriersQuery, IReadOnlyList<CarrierView>>
{
    public ValueTask<IReadOnlyList<CarrierView>> Handle(ListActiveCarriersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<CarrierView> active = [.. carriers.ListActive().Select(carrier => new CarrierView(carrier.Id.Value, carrier.Name))];
        return ValueTask.FromResult(active);
    }
}
