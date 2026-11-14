using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.Drivers;

namespace Norvia.Erp.Application.MasterData.Drivers.List;

public sealed class ListDriversHandler(IReadRepository<Driver> drivers) : IQueryHandler<ListDriversQuery, IReadOnlyList<DriverView>>
{
    public async ValueTask<IReadOnlyList<DriverView>> Handle(ListDriversQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await drivers.ListAsync(new DriversSpec(), cancellationToken);
        return [.. all.Select(driver => new DriverView(driver.Id.Value, driver.Name, [.. driver.Licences.Select(licence => licence.ToString())], driver.Active, driver.Revision))];
    }
}
