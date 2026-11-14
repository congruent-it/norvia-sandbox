using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Application.MasterData.Vehicles.List;

public sealed class ListVehiclesHandler(IReadRepository<Vehicle> vehicles) : IQueryHandler<ListVehiclesQuery, IReadOnlyList<VehicleView>>
{
    public async ValueTask<IReadOnlyList<VehicleView>> Handle(ListVehiclesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await vehicles.ListAsync(new VehiclesSpec(), cancellationToken);
        return [.. all.Select(vehicle => new VehicleView(vehicle.Id.Value, vehicle.Registration, vehicle.Type.ToString(), vehicle.PayloadKg, vehicle.PalletPlaces, vehicle.Active, vehicle.Revision))];
    }
}
