using Mediator;

namespace Norvia.PartnerPortal.Application.Faults.List;

public sealed class ListFaultsHandler(IFaultSwitches faults) : IQueryHandler<ListFaultsQuery, IReadOnlyList<FaultView>>
{
    public async ValueTask<IReadOnlyList<FaultView>> Handle(ListFaultsQuery query, CancellationToken cancellationToken) =>
        await faults.ListAsync(cancellationToken);
}
