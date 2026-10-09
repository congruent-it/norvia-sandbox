using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.ListDrivers;

public sealed class ListDriversHandler(IDriverDirectory drivers) : IQueryHandler<ListDriversQuery, IReadOnlyList<DriverView>>
{
    public ValueTask<IReadOnlyList<DriverView>> Handle(ListDriversQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<DriverView> active =
        [
            .. drivers.ListActive().Select(driver => new DriverView(
                driver.Id.Value,
                driver.Name,
                [.. driver.Licences.Order()],
                [.. Enum.GetValues<VehicleType>().Where(driver.CanDrive)])),
        ];
        return ValueTask.FromResult(active);
    }
}
