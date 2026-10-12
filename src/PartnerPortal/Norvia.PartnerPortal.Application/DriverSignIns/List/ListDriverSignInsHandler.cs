using Mediator;

namespace Norvia.PartnerPortal.Application.DriverSignIns.List;

public sealed class ListDriverSignInsHandler(IDriverSignInDirectory signIns) : IQueryHandler<ListDriverSignInsQuery, DriverSignInsView>
{
    public ValueTask<DriverSignInsView> Handle(ListDriverSignInsQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(signIns.Current);
}
