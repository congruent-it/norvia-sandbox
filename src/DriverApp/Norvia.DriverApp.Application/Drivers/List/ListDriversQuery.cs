using Mediator;

namespace Norvia.DriverApp.Application.Drivers.List;

public sealed record ListDriversQuery : IQuery<IReadOnlyList<DriverView>>;

public sealed class ListDriversHandler(IDriverDirectory drivers) : IQueryHandler<ListDriversQuery, IReadOnlyList<DriverView>>
{
    public ValueTask<IReadOnlyList<DriverView>> Handle(ListDriversQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(drivers.All);
}
